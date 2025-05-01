// src/ws/client.rs
use actix::{Actor, ActorContext, StreamHandler};
use actix_web_actors::ws;

use crate::types::PlayerId;

pub struct WsClient {
    pub id: PlayerId,
    pub name: Option<String>,
}

impl WsClient {
    pub fn new(id: PlayerId) -> Self {
        Self { id, name: None }
    }
}

impl Actor for WsClient {
    type Context = ws::WebsocketContext<Self>;
}

impl StreamHandler<Result<ws::Message, ws::ProtocolError>> for WsClient {
    fn handle(&mut self, msg: Result<ws::Message, ws::ProtocolError>, ctx: &mut Self::Context) {
        match msg {
            Ok(ws::Message::Ping(payload)) => {
                ctx.pong(&payload);
            }
            Ok(ws::Message::Text(text)) => {
                println!("Received text from {}: {}", self.id, text);
                // placeholder — forward to message router later
            }
            Ok(ws::Message::Close(reason)) => {
                println!("Client {} disconnected: {:?}", self.id, reason);
                ctx.stop();
            }
            _ => {}
        }
    }
}
