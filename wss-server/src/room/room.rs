use std::collections::HashMap;
use std::sync::{Arc, Mutex};

use actix::Addr;
use serde_json::json;

use crate::{
    types::{Phase, Player, PlayerId, Role},
    ws::client::{ServerText, WsClient},
};

/// Shared, thread-safe handle to your single Room
pub type SharedRoom = Arc<Mutex<Room>>;

pub struct Room {
    players: HashMap<PlayerId, Player>,
    phase: Phase,
    round: u32,
    game_started: bool,
    pending_night: HashMap<PlayerId, (String, String)>,
}

impl Room {
    pub fn new() -> Self {
        Self {
            players: HashMap::new(),
            phase: Phase::Lobby,
            round: 0,
            game_started: false,
            pending_night: HashMap::new(),
        }
    }

    /// 1️⃣ Add a player into the room
    pub fn add_player(&mut self, id: PlayerId, name: String, addr: Addr<WsClient>) {
        println!("→ join {} ({})", id, name);

        // Insert or update the Player entry
        self.players.insert(
            id.clone(),
            Player {
                id,
                name,
                role: None,
                is_ready: false,
                is_alive: true,
                addr,
            },
        );

        // 2️⃣ Broadcast the updated lobby state
        self.broadcast_lobby();
    }

    /// Send everyone the current list of players & ready flags
    fn broadcast_lobby(&self) {
        // Build a JSON string matching your protocol:
        // { type:1, target:"lobby", arguments:[{ players: [ {id,name,ready}, … ] }] }
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

        // Send to each connected client actor
        for player in self.players.values() {
            player.addr.do_send(ServerText(snapshot.clone()));
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

        // Count ready players
        let ready_count = self.players.values().filter(|p| p.is_ready).count();
        if ready_count == 4 {
            self.game_started = true;
            self.start_game();
        }
    }

    fn start_game(&mut self) {
        use crate::game::role::assign_roles;

        // 1️⃣ Collect player IDs
        let ids: Vec<_> = self.players.keys().cloned().collect();

        // 2️⃣ Assign roles
        let role_map = assign_roles(&ids);

        // 3️⃣ Update each Player struct
        for (id, role) in &role_map {
            if let Some(player) = self.players.get_mut(id) {
                player.role = Some(*role);
            }
        }

        // 4️⃣ Send private role to each player
        for (id, role) in &role_map {
            let payload = serde_json::json!({ "role": format!("{role:?}") });
            let frame = serde_json::json!({
                "type": 1,
                "target": "role",
                "arguments": [ payload ]
            })
            .to_string();

            if let Some(player) = self.players.get(id) {
                player.addr.do_send(crate::ws::client::ServerText(frame));
            }
        }

        // 5️⃣ Broadcast gameStart with full players list
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
            p.addr
                .do_send(crate::ws::client::ServerText(start_frame.clone()));
        }

        // 6️⃣ Broadcast initial phase: night round 1
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
            p.addr
                .do_send(crate::ws::client::ServerText(phase_frame.clone()));
        }
    }

    pub fn night_action(&mut self, id: PlayerId, action: String, target: String) {
        // Only accept during Night phase
        if self.phase != Phase::Night {
            return;
        }
        println!("→ nightAction from {}: {} {}", id, action, target);
        if self.pending_night.len() == self.required_night_actions() {
            self.resolve_night();
        }
    }

    fn required_night_actions(&self) -> usize {
        // 1 Werewolf + 1 Seer if they’re alive
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
        // 1️⃣  Determine kill target (first "kill" we find)
        let mut killed: Option<PlayerId> = None;
        for (actor, (action, target)) in &self.pending_night {
            if action == "kill" {
                killed = Some(target.clone());
            }
        }

        // 2️⃣  Apply kill
        if let Some(ref id) = killed {
            if let Some(victim) = self.players.get_mut(id) {
                victim.is_alive = false;
            }
        }

        // 3️⃣  Send peekResult privately to the seer
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
                        seer.addr.do_send(crate::ws::client::ServerText(peek_frame));
                    }
                }
            }
        }

        // 4️⃣  Broadcast nightEnd to everyone
        let night_end = serde_json::json!({
            "type": 1,
            "target": "nightEnd",
            "arguments": [{
                "killed": killed
            }]
        })
        .to_string();
        for p in self.players.values() {
            p.addr
                .do_send(crate::ws::client::ServerText(night_end.clone()));
        }

        // 5️⃣  Clear state, flip to Day
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
            p.addr
                .do_send(crate::ws::client::ServerText(day_frame.clone()));
        }
    }
}
