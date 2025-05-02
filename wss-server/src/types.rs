use crate::ws::client::WsClient;
use actix::Addr;
use std::collections::HashMap;

pub type PlayerId = String;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, serde::Serialize)]
pub enum Role {
    Werewolf,
    Seer,
    Villager,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, serde::Serialize)]
pub enum Phase {
    Lobby,
    Night,
    Day,
    GameOver,
}

#[derive(Debug)]
pub struct Player {
    pub id: PlayerId,
    pub name: String,
    pub role: Option<Role>,
    pub is_ready: bool,
    pub is_alive: bool,
    pub addr: Option<Addr<WsClient>>,
}

pub type VoteMap = HashMap<PlayerId, PlayerId>;
