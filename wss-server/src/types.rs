use crate::ws::client::WsClient;
use actix::Addr;
use std::collections::HashMap;

/// Unique player ID (e.g., "p1", "p2", ...)
pub type PlayerId = String;

/// Role assigned at game start
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, serde::Serialize)]
pub enum Role {
    Werewolf,
    Seer,
    Villager,
}

/// Current game phase
#[derive(Debug, Clone, Copy, PartialEq, Eq, serde::Serialize)]
pub enum Phase {
    Lobby,
    Night,
    Day,
    Over,
}

/// Struct to hold player state
#[derive(Debug)]
pub struct Player {
    pub id: PlayerId,
    pub name: String,
    pub role: Option<Role>,
    pub is_ready: bool,
    pub is_alive: bool,
    pub addr: Addr<WsClient>,
}

/// Struct to track vote mapping (player → target)
pub type VoteMap = HashMap<PlayerId, PlayerId>;
