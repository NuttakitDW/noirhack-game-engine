use std::collections::HashMap;
use std::sync::{Arc, Mutex};

use actix::Addr;
use serde_json::{json, Value};

use crate::{
    types::{Phase, Player, PlayerId, Role, VoteMap},
    ws::client::{ServerText, WsClient},
};

pub type SharedRoom = Arc<Mutex<Room>>;

pub struct Room {
    pub players: HashMap<PlayerId, Player>,
    phase: Phase,
    round: u32,
    game_started: bool,
    pending_night: HashMap<PlayerId, (String, String)>,
    votes: VoteMap,
    pub public_keys: HashMap<PlayerId, String>,
    pub shuffle_order: Vec<PlayerId>,
    pub shuffle_index: usize,
    pub agg_pk: String,
    pub deck_state: Vec<[String; 2]>,
}

impl Room {
    pub fn new() -> Self {
        println!("Room::new");
        Self {
            players: HashMap::new(),
            phase: Phase::Lobby,
            round: 0,
            game_started: false,
            pending_night: HashMap::new(),
            votes: HashMap::new(),
            public_keys: HashMap::new(),
            shuffle_order: Vec::new(),
            shuffle_index: 0,
            agg_pk: String::new(),
            deck_state: vec![
                ["1".into(), "0".into()], // Wolf
                ["1".into(), "1".into()], // Seer
                ["1".into(), "2".into()], // Villager
                ["1".into(), "2".into()], // Villager
            ],
        }
    }
    pub fn register_public_key(&mut self, player_id: &PlayerId, pk: String) {
        // you can log here to debug:
        println!("Room::register_public_key id={} pk={}", player_id, pk);
        self.public_keys.insert(player_id.clone(), pk);
    }

    pub fn add_player(&mut self, id: PlayerId, name: String, addr: Addr<WsClient>) {
        println!("Room::add_player id={} name={} addr added", id, name);
        self.players.insert(
            id.clone(),
            Player {
                id,
                name,
                role: None,
                is_ready: false,
                is_alive: true,
                addr: Some(addr),
            },
        );
        self.broadcast_lobby();
    }

    fn broadcast_lobby(&self) {
        println!("Room::broadcast_lobby players={}", self.players.len());
        let snapshot = json!({
            "type": 1,
            "target": "lobby",
            "arguments": [{
                "players": self.players.values().map(|p| json!({
                    "id": p.id,
                    "name": p.name,
                    "ready": p.is_ready
                })).collect::<Vec<_>>()
            }]
        })
        .to_string();
        for player in self.players.values() {
            if let Some(addr) = &player.addr {
                addr.do_send(ServerText(snapshot.clone()));
            }
        }
    }

    pub fn set_ready(&mut self, id: PlayerId, ready: bool) {
        println!("Room::set_ready id={} ready={}", id, ready);
        if let Some(player) = self.players.get_mut(&id) {
            player.is_ready = ready;
            self.broadcast_lobby();
            self.try_start();
        } else {
            println!("Room::set_ready unknown id={}", id);
        }
    }

    fn try_start(&mut self) {
        println!(
            "Room::try_start game_started={} ready_count={}",
            self.game_started,
            self.players.values().filter(|p| p.is_ready).count()
        );
        if self.game_started {
            return;
        }
        let ready_count = self.players.values().filter(|p| p.is_ready).count();
        if ready_count == 4 {
            self.game_started = true;
            self.start_game();
        }
    }

    fn start_game(&mut self) {
        println!("Room::start_game");
        use crate::game::role::assign_roles;
        let ids: Vec<_> = self.players.keys().cloned().collect();
        let role_map = assign_roles(&ids);
        for (id, role) in &role_map {
            if let Some(player) = self.players.get_mut(id) {
                player.role = Some(*role);
            }
        }
        for (id, role) in &role_map {
            let frame = json!({
                "type":1,
                "target":"role",
                "arguments":[{"role":format!("{:?}",role)}]
            })
            .to_string();
            if let Some(player) = self.players.get(id) {
                if let Some(addr) = &player.addr {
                    addr.do_send(ServerText(frame.clone()));
                }
            }
        }
        let players_info: Vec<_> = self
            .players
            .values()
            .map(|p| {
                json!({
                    "id": p.id,
                    "name": p.name
                })
            })
            .collect();
        let start_frame = json!({
            "type":1,
            "target":"gameStart",
            "arguments":[{"players":players_info}]
        })
        .to_string();
        for p in self.players.values() {
            if let Some(addr) = &p.addr {
                addr.do_send(crate::ws::client::ServerText(start_frame.clone()));
            }
        }
        self.phase = Phase::Night;
        self.round = 1;
        let phase_frame = json!({
            "type":1,
            "target":"phase",
            "arguments":[{"phase":"night","round":self.round,"duration":30}]
        })
        .to_string();
        for p in self.players.values() {
            if let Some(addr) = &p.addr {
                addr.do_send(crate::ws::client::ServerText(phase_frame.clone()));
            }
        }
    }

    pub fn night_action(&mut self, id: PlayerId, action: String, target: String) {
        println!(
            "Room::night_action id={} action={} target={}",
            id, action, target
        );
        if self.phase != Phase::Night {
            return;
        }
        self.pending_night.insert(id.clone(), (action, target));
        if self.pending_night.len() == self.required_night_actions() {
            self.resolve_night();
        }
    }

    fn required_night_actions(&self) -> usize {
        let count = self
            .players
            .values()
            .filter(|p| p.is_alive)
            .filter(|p| matches!(p.role, Some(Role::Werewolf) | Some(Role::Seer)))
            .count();
        println!("Room::required_night_actions count={}", count);
        count
    }

    fn resolve_night(&mut self) {
        println!("Room::resolve_night pending={}", self.pending_night.len());
        let mut killed: Option<PlayerId> = None;
        for (action, target) in self.pending_night.values() {
            if action == "kill" {
                killed = Some(target.clone());
            }
        }
        if let Some(ref id) = killed {
            println!("Room::resolve_night killed={}", id);
            if let Some(victim) = self.players.get_mut(id) {
                victim.is_alive = false;
            }
        }
        for (actor, (action, target)) in &self.pending_night {
            if action == "peek" {
                println!("Room::resolve_night peek actor={} target={}", actor, target);
                if let Some(seer) = self.players.get(actor) {
                    if let Some(target_player) = self.players.get(target) {
                        let peek_frame = json!({
                            "type":1,
                            "target":"peekResult",
                            "arguments":[{"target":target,"role":format!("{:?}",target_player.role.unwrap())}]
                        })
                        .to_string();
                        if let Some(addr) = &seer.addr {
                            addr.do_send(crate::ws::client::ServerText(peek_frame));
                        }
                    }
                }
            }
        }
        let night_end = json!({
            "type":1,
            "target":"nightEnd",
            "arguments":[{"killed":killed}]
        })
        .to_string();
        println!("Room::resolve_night broadcast nightEnd");
        for p in self.players.values() {
            if let Some(addr) = &p.addr {
                addr.do_send(crate::ws::client::ServerText(night_end.clone()));
            }
        }
        if let Some(winner) = self.check_win() {
            println!("Room::resolve_night win={}", winner);
            self.broadcast_game_over(winner);
            self.phase = Phase::GameOver;
            return;
        }
        self.pending_night.clear();
        self.phase = Phase::Day;
        let day_frame = json!({
            "type":1,
            "target":"phase",
            "arguments":[{"phase":"day","round":self.round,"duration":60}]
        })
        .to_string();
        println!("Room::resolve_night broadcast phase=day");
        for p in self.players.values() {
            if let Some(addr) = &p.addr {
                addr.do_send(crate::ws::client::ServerText(day_frame.clone()));
            }
        }
    }

    fn living_count(&self) -> usize {
        let count = self.players.values().filter(|p| p.is_alive).count();
        println!("Room::living_count ={}", count);
        count
    }

    pub fn vote(&mut self, voter: PlayerId, target: PlayerId) {
        println!("Room::vote voter={} target={}", voter, target);
        if self.phase != Phase::Day {
            return;
        }
        if !self.players.get(&voter).map_or(false, |p| p.is_alive) {
            return;
        }
        self.votes.insert(voter.clone(), target.clone());
        let tally_frame =
            json!({"type":1,"target":"voteUpdate","arguments":[self.votes]}).to_string();
        println!("Room::vote broadcast voteUpdate");
        for p in self.players.values() {
            if let Some(addr) = &p.addr {
                addr.do_send(ServerText(tally_frame.clone()));
            }
        }
        if self.votes.len() == self.living_count() {
            self.resolve_day();
        }
    }

    fn resolve_day(&mut self) {
        println!(
            "Room::resolve_day votes={} pending_night={} players_alive={}",
            self.votes.len(),
            self.pending_night.len(),
            self.living_count()
        );
        let mut counts: HashMap<&PlayerId, usize> = HashMap::new();
        for tgt in self.votes.values() {
            *counts.entry(tgt).or_default() += 1;
        }
        let (lynched, _max) = counts
            .iter()
            .max_by_key(|(_, c)| *c)
            .map(|(id, c)| ((*id).clone(), *c))
            .unwrap_or((String::new(), 0));
        let lynch_opt = if _max > 0 && counts.values().filter(|&&c| c == _max).count() == 1 {
            Some(lynched.clone())
        } else {
            None
        };
        if let Some(id) = &lynch_opt {
            println!("Room::resolve_day lynched={}", id);
            if let Some(p) = self.players.get_mut(id) {
                p.is_alive = false;
            }
        } else {
            println!("Room::resolve_day lynch skip");
        }
        let frame =
            json!({"type":1,"target":"dayEnd","arguments":[{"lynched":lynch_opt}]}).to_string();
        println!("Room::resolve_day broadcast dayEnd");
        for p in self.players.values() {
            if let Some(addr) = &p.addr {
                addr.do_send(ServerText(frame.clone()));
            }
        }
        if let Some(winner) = self.check_win() {
            println!("Room::resolve_day win={}", winner);
            self.broadcast_game_over(winner);
            self.phase = Phase::GameOver;
            return;
        }
        self.votes.clear();
        self.pending_night.clear();
        self.phase = Phase::Night;
        self.round += 1;
        let night_frame = json!({"type":1,"target":"phase","arguments":[{"phase":"night","round":self.round,"duration":30}]}).to_string();
        println!("Room::resolve_day broadcast phase=night");
        for p in self.players.values() {
            if let Some(addr) = &p.addr {
                addr.do_send(ServerText(night_frame.clone()));
            }
        }
    }

    fn check_win(&self) -> Option<&'static str> {
        let wolf_alive = self
            .players
            .values()
            .any(|p| p.is_alive && p.role == Some(Role::Werewolf));
        let villagers_alive = self
            .players
            .values()
            .any(|p| p.is_alive && p.role != Some(Role::Werewolf));
        let result = match (wolf_alive, villagers_alive) {
            (false, true) => Some("villagers"),
            (true, false) => Some("werewolves"),
            _ => None,
        };
        println!("Room::check_win -> {:?}", result);
        result
    }

    fn broadcast_game_over(&self, winner: &str) {
        println!("Room::broadcast_game_over winner={}", winner);
        let role_map = self
            .players
            .iter()
            .map(|(id, p)| (id.clone(), Value::String(format!("{:?}", p.role.unwrap()))))
            .collect::<serde_json::Map<_, _>>();
        let frame =
            json!({"type":1,"target":"gameOver","arguments":[{"winner":winner,"roles":role_map}]})
                .to_string();
        println!("Room::broadcast_game_over frame={}", frame);
        for p in self.players.values() {
            if let Some(addr) = &p.addr {
                addr.do_send(ServerText(frame.clone()));
            }
        }
    }
    pub fn chat(&self, id: PlayerId, text: String) {
        println!("Room::chat id={} phase={:?}", id, self.phase);
        if self.phase == Phase::Night {
            println!("Room::chat ignored (night)");
            return;
        }
        if let Some(sender) = self.players.get(&id) {
            if !sender.is_alive {
                println!("Room::chat ignored (dead)");
                return;
            }
        } else {
            println!("Room::chat ignored (unknown)");
            return;
        }
        let frame =
            json!({"type":1,"target":"chat","arguments":[{"from":id,"text":text}]}).to_string();
        println!("Room::chat broadcast frame={}", frame);
        for p in self.players.values() {
            if p.is_alive {
                if let Some(addr) = &p.addr {
                    addr.do_send(ServerText(frame.clone()));
                }
            }
        }
    }
    pub fn initiate_shuffle(&mut self) {
        self.shuffle_order = self.players.keys().cloned().collect();
        self.shuffle_index = 0;

        let frame = json!({
            "type": 1,
            "target": "startShuffle",
            "arguments": [{
                "agg_pk":   self.agg_pk,
                "deck":     self.deck_state
            }]
        })
        .to_string();

        if let Some(player_id) = self.shuffle_order.get(0) {
            if let Some(player) = self.players.get(player_id) {
                if let Some(addr) = &player.addr {
                    addr.do_send(ServerText(frame));
                }
            }
        }
    }
}
