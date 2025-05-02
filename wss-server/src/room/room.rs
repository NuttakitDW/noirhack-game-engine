use std::collections::HashMap;
use std::sync::{Arc, Mutex};

use actix::Addr;
use serde_json::json;

use crate::{
    types::{Phase, Player, PlayerId},
    ws::client::{ServerText, WsClient},
};

/// Shared, thread-safe handle to your single Room
pub type SharedRoom = Arc<Mutex<Room>>;

pub struct Room {
    players: HashMap<PlayerId, Player>,
    phase: Phase,
    round: u32,
}

impl Room {
    pub fn new() -> Self {
        Self {
            players: HashMap::new(),
            phase: Phase::Lobby,
            round: 0,
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
}
