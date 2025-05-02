// src/ws/client.rs
use actix::{Actor, ActorContext, StreamHandler};
use actix_web_actors::ws;

use crate::{message, room::room::SharedRoom, types::PlayerId};
use serde::de::Error;

use actix::Handler;
use actix::Message;

pub struct WsClient {
    pub id: PlayerId,
    room: SharedRoom, // NEW – store the shared room handle
}

impl WsClient {
    // Now takes the room handle
    pub fn new(id: PlayerId, room: SharedRoom) -> Self {
        Self { id, room }
    }
}

#[derive(Message)]
#[rtype(result = "()")]
pub struct ServerText(pub String);

impl Handler<ServerText> for WsClient {
    type Result = ();

    fn handle(&mut self, msg: ServerText, ctx: &mut Self::Context) {
        ctx.text(msg.0);
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
            Ok(ws::Message::Text(raw)) => {
                match serde_json::from_str::<message::Incoming>(&raw).and_then(|inc| {
                    message::to_client_event(inc).map_err(|e| serde_json::Error::custom(e))
                }) {
                    Ok(event) => {
                        println!("Client {} sent event: {:?}", self.id, event);
                        // TODO: forward to Room once we have it
                    }
                    Err(err) => {
                        println!("JSON error from {}: {err}", self.id);
                        // you could ctx.text(...) an error frame back
                    }
                }
            }
            Ok(ws::Message::Close(reason)) => {
                println!("Client {} disconnected: {:?}", self.id, reason);
                ctx.stop();
            }
            _ => {}
        }
    }
}
