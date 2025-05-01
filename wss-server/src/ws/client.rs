// src/ws/client.rs
use actix::prelude::*;

pub struct WsClient;

impl Actor for WsClient {
    type Context = Context<Self>;
}
