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
    players: HashMap<PlayerId, Player>,
    phase: Phase,
    round: u32,
    game_started: bool,
    pending_night: HashMap<PlayerId, (String, String)>,
    votes: VoteMap,
}

impl Room {
    pub fn new() -> Self {
        Self {
            players: HashMap::new(),
            phase: Phase::Lobby,
            round: 0,
            game_started: false,
            pending_night: HashMap::new(),
            votes: HashMap::new(),
        }
    }

    pub fn add_player(&mut self, id: PlayerId, name: String, addr: Addr<WsClient>) {
        println!("→ join {} ({})", id, name);

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
        let snapshot = json!({
            "type": 1,
            "target": "lobby",
            "arguments": [{
                "players": self.players.values().map(|p| {
                    json!({
                        "id": p.id,
                        "name": p.name,
                        "ready": p.is_ready
                    })
                }).collect::<Vec<_>>()
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
        if let Some(player) = self.players.get_mut(&id) {
            player.is_ready = ready;
            println!("→ ready toggle {} = {}", id, ready);
            self.broadcast_lobby();
            self.try_start();
        } else {
            println!("→ tried to set_ready for unknown id {}", id);
        }
    }
    fn try_start(&mut self) {
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
        use crate::game::role::assign_roles;

        let ids: Vec<_> = self.players.keys().cloned().collect();

        let role_map = assign_roles(&ids);

        for (id, role) in &role_map {
            if let Some(player) = self.players.get_mut(id) {
                player.role = Some(*role);
            }
        }

        for (id, role) in &role_map {
            let payload = serde_json::json!({ "role": format!("{role:?}") });
            let frame = serde_json::json!({
                "type": 1,
                "target": "role",
                "arguments": [ payload ]
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
            .map(|p| serde_json::json!({ "id": p.id, "name": p.name }))
            .collect();
        let start_frame = serde_json::json!({
            "type": 1,
            "target": "gameStart",
            "arguments": [{ "players": players_info }]
        })
        .to_string();
        for p in self.players.values() {
            if let Some(addr) = &p.addr {
                addr.do_send(crate::ws::client::ServerText(start_frame.clone()));
            }
        }

        self.phase = Phase::Night;
        self.round = 1;
        let phase_frame = serde_json::json!({
            "type": 1,
            "target": "phase",
            "arguments": [{
                "phase": "night",
                "round": self.round,
                "duration": 30
            }]
        })
        .to_string();
        for p in self.players.values() {
            if let Some(addr) = &p.addr {
                addr.do_send(crate::ws::client::ServerText(phase_frame.clone()));
            }
        }
    }

    pub fn night_action(&mut self, id: PlayerId, action: String, target: String) {
        if self.phase != Phase::Night {
            return;
        }

        println!("→ nightAction from {}: {} {}", id, action, target);

        self.pending_night.insert(id.clone(), (action, target));

        if self.pending_night.len() == self.required_night_actions() {
            self.resolve_night();
        }
    }

    fn required_night_actions(&self) -> usize {
        let mut n = 0;
        for p in self.players.values() {
            if !p.is_alive {
                continue;
            }
            match p.role {
                Some(Role::Werewolf) => n += 1,
                Some(Role::Seer) => n += 1,
                _ => {}
            }
        }
        n
    }

    fn resolve_night(&mut self) {
        let mut killed: Option<PlayerId> = None;
        for (action, target) in self.pending_night.values() {
            if action == "kill" {
                killed = Some(target.clone());
            }
        }

        if let Some(ref id) = killed {
            if let Some(victim) = self.players.get_mut(id) {
                victim.is_alive = false;
            }
        }

        for (actor, (action, target)) in &self.pending_night {
            if action == "peek" {
                if let Some(seer) = self.players.get(actor) {
                    if let Some(target_player) = self.players.get(target) {
                        let peek_frame = serde_json::json!({
                            "type": 1,
                            "target": "peekResult",
                            "arguments": [{
                                "target": target,
                                "role": format!("{:?}", target_player.role.unwrap())
                            }]
                        })
                        .to_string();
                        if let Some(addr) = &seer.addr {
                            addr.do_send(crate::ws::client::ServerText(peek_frame));
                        }
                    }
                }
            }
        }

        let night_end = serde_json::json!({
            "type": 1,
            "target": "nightEnd",
            "arguments": [{
                "killed": killed
            }]
        })
        .to_string();
        for p in self.players.values() {
            if let Some(addr) = &p.addr {
                addr.do_send(crate::ws::client::ServerText(night_end.clone()));
            }
        }

        if let Some(winner) = self.check_win() {
            self.broadcast_game_over(winner);
            self.phase = Phase::GameOver;
            return;
        }

        self.pending_night.clear();
        self.phase = Phase::Day;

        let day_frame = serde_json::json!({
            "type": 1,
            "target": "phase",
            "arguments": [{
                "phase": "day",
                "round": self.round,
                "duration": 60
            }]
        })
        .to_string();
        for p in self.players.values() {
            if let Some(addr) = &p.addr {
                addr.do_send(crate::ws::client::ServerText(day_frame.clone()));
            }
        }
    }

    fn living_count(&self) -> usize {
        self.players.values().filter(|p| p.is_alive).count()
    }

    pub fn vote(&mut self, voter: PlayerId, target: PlayerId) {
        if self.phase != Phase::Day {
            return;
        }
        if !self.players.get(&voter).map_or(false, |p| p.is_alive) {
            return;
        }

        self.votes.insert(voter.clone(), target.clone());

        let tally_frame = serde_json::json!({
            "type": 1,
            "target": "voteUpdate",
            "arguments": [ self.votes ]
        })
        .to_string();
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
            if let Some(p) = self.players.get_mut(id) {
                p.is_alive = false;
            }
        }

        let frame = serde_json::json!({
            "type": 1,
            "target": "dayEnd",
            "arguments": [{ "lynched": lynch_opt }]
        })
        .to_string();
        for p in self.players.values() {
            if let Some(addr) = &p.addr {
                addr.do_send(ServerText(frame.clone()));
            }
        }

        if let Some(winner) = self.check_win() {
            self.broadcast_game_over(winner);
            self.phase = Phase::GameOver;
            return;
        }

        self.votes.clear();
        self.pending_night.clear();
        self.phase = Phase::Night;
        self.round += 1;

        let night_frame = serde_json::json!({
            "type": 1,
            "target": "phase",
            "arguments": [{
                "phase":"night",
                "round": self.round,
                "duration": 30
            }]
        })
        .to_string();
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
        match (wolf_alive, villagers_alive) {
            (false, true) => Some("villagers"),
            (true, false) => Some("werewolves"),
            _ => None,
        }
    }

    fn broadcast_game_over(&self, winner: &str) {
        let role_map = self
            .players
            .iter()
            .map(|(id, p)| {
                (
                    id.clone(),
                    Value::String(format!("{:?}", p.role.unwrap())), // wrap into JSON value
                )
            })
            .collect::<serde_json::Map<_, _>>();

        let frame = serde_json::json!({
            "type": 1,
            "target": "gameOver",
            "arguments": [{
                "winner": winner,
                "roles":  role_map
            }]
        })
        .to_string();

        for p in self.players.values() {
            if let Some(addr) = &p.addr {
                addr.do_send(ServerText(frame.clone()));
            }
        }
    }
}

#[cfg(test)]
mod day_tests {
    use super::*;
    use crate::types::{Phase, Player, Role};

    fn make_player(id: &str, role: Role) -> Player {
        Player {
            id: id.to_string(),
            name: id.to_string(),
            role: Some(role),
            is_ready: true,
            is_alive: true,
            addr: None,
        }
    }

    #[test]
    fn majority_lynch_kills_player_and_flips_to_night() {
        let mut room = Room::new();
        room.players
            .insert("wolf".into(), make_player("wolf", Role::Werewolf));
        room.players
            .insert("seer".into(), make_player("seer", Role::Seer));
        room.players
            .insert("v1".into(), make_player("v1", Role::Villager));
        room.players
            .insert("v2".into(), make_player("v2", Role::Villager));

        room.phase = Phase::Day;
        room.round = 1;

        room.vote("wolf".into(), "seer".into());
        room.vote("v1".into(), "seer".into());
        room.vote("seer".into(), "wolf".into());

        assert!(room.players["seer"].is_alive);
        assert!(room.players["wolf"].is_alive);

        assert_eq!(room.phase, Phase::Day);

        room.vote("v2".into(), "seer".into());

        assert!(!room.players["seer"].is_alive, "seer should be lynched");
        assert_eq!(room.phase, Phase::Night, "phase flips to night");
        assert_eq!(room.round, 2, "round incremented");
    }

    #[test]
    fn tie_vote_keeps_everyone_alive_and_flips_to_night() {
        let mut room = Room::new();
        room.players
            .insert("wolf".into(), make_player("wolf", Role::Werewolf));
        room.players
            .insert("seer".into(), make_player("seer", Role::Seer));
        room.players
            .insert("v1".into(), make_player("v1", Role::Villager));
        room.players
            .insert("v2".into(), make_player("v2", Role::Villager));

        room.phase = Phase::Day;
        room.round = 1;

        room.vote("wolf".into(), "seer".into());
        room.vote("v1".into(), "seer".into());
        room.vote("seer".into(), "wolf".into());
        room.vote("v2".into(), "wolf".into());

        assert!(room.players["seer"].is_alive);
        assert!(room.players["wolf"].is_alive);

        assert_eq!(room.phase, Phase::Night);
        assert_eq!(room.round, 2);
    }
}

#[cfg(test)]
mod win_tests {
    use super::*;
    use crate::types::{Player, Role};

    fn mk_player(id: &str, role: Role, alive: bool) -> Player {
        Player {
            id: id.into(),
            name: id.into(),
            role: Some(role),
            is_ready: true,
            is_alive: alive,
            addr: None, // no socket needed in unit tests
        }
    }

    #[test]
    fn villagers_win_when_wolf_dead() {
        let mut room = Room::new();
        room.players
            .insert("wolf".into(), mk_player("wolf", Role::Werewolf, false)); // dead wolf
        room.players
            .insert("seer".into(), mk_player("seer", Role::Seer, true));
        room.players
            .insert("v1".into(), mk_player("v1", Role::Villager, true));

        assert_eq!(room.check_win(), Some("villagers"));
    }

    #[test]
    fn wolves_win_when_no_villagers_alive() {
        let mut room = Room::new();
        room.players
            .insert("wolf".into(), mk_player("wolf", Role::Werewolf, true));
        room.players
            .insert("seer".into(), mk_player("seer", Role::Seer, false));
        room.players
            .insert("v1".into(), mk_player("v1", Role::Villager, false));

        assert_eq!(room.check_win(), Some("werewolves"));
    }

    #[test]
    fn check_win_returns_none_when_game_continues() {
        let mut room = Room::new();
        room.players
            .insert("wolf".into(), mk_player("wolf", Role::Werewolf, true));
        room.players
            .insert("seer".into(), mk_player("seer", Role::Seer, true));
        room.players
            .insert("v1".into(), mk_player("v1", Role::Villager, true));

        assert_eq!(room.check_win(), None);
    }
}
