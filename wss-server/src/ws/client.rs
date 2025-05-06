use actix::AsyncContext;
use actix::{Actor, ActorContext, Handler, Message, StreamHandler};
use actix_web_actors::ws;
use serde_json::json;

use crate::utils::aggregate_public_keys;
use crate::{room::room::SharedRoom, types::PlayerId};

pub struct WsClient {
    pub id: PlayerId,
    room: SharedRoom,
}

impl WsClient {
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

impl WsClient {
    fn handle_text(&mut self, raw: String, ctx: &mut ws::WebsocketContext<Self>) {
        use crate::message::{to_client_event, ClientEvent, Incoming};

        match serde_json::from_str::<Incoming>(&raw) {
            Ok(inc) => match to_client_event(inc) {
                Ok(ClientEvent::Join { name }) => {
                    let addr = ctx.address();
                    self.room
                        .lock()
                        .unwrap()
                        .add_player(self.id.clone(), name, addr);
                }
                Ok(ClientEvent::Ready(flag)) => {
                    self.room.lock().unwrap().set_ready(self.id.clone(), flag);
                }
                Ok(ClientEvent::NightAction { action, target }) => {
                    self.room
                        .lock()
                        .unwrap()
                        .night_action(self.id.clone(), action, target);
                }
                Ok(ClientEvent::Vote { target }) => {
                    self.room.lock().unwrap().vote(self.id.clone(), target);
                }
                Ok(ClientEvent::Chat { text }) => {
                    self.room.lock().unwrap().chat(self.id.clone(), text);
                }
                Ok(ClientEvent::RegisterPublicKey { public_key }) => {
                    let mut room = self.room.lock().unwrap();
                    room.register_public_key(&self.id, public_key.clone());

                    let ack = ServerText(
                        serde_json::to_string(&serde_json::json!({
                            "type":1,
                            "target":"publicKeyRegistered",
                            "arguments":[{ "status":"ok" }]
                        }))
                        .unwrap(),
                    );
                    ctx.text(ack.0);

                    let num_players = room.players.len();
                    let num_keys = room.public_keys.len();

                    if num_players == 4 && num_keys == 4 {
                        // call into your new util
                        let agg_pk =
                            aggregate_public_keys(&room).expect("failed to aggregate public keys");

                        // put the aggregated key into deck_state (we’ll treat this as
                        // the first “deck” for startShuffle)
                        room.deck_state = vec![agg_pk.clone()];

                        // send startShuffle to the first player
                        room.initiate_shuffle();
                    }
                }
                Ok(ClientEvent::ShuffleDone { deck }) => {
                    // 4.1 Lock the room
                    let mut room = self.room.lock().unwrap();

                    // 4.2 Validate it’s this client’s turn
                    if room.shuffle_index >= room.shuffle_order.len()
                        || room.shuffle_order[room.shuffle_index] != self.id
                    {
                        return;
                    }

                    // 4.3 Update the deck_state
                    room.deck_state = deck.clone();

                    // 4.4 Advance the index
                    room.shuffle_index += 1;

                    // 4.5 Next player or complete
                    if room.shuffle_index < room.shuffle_order.len() {
                        let next_id = &room.shuffle_order[room.shuffle_index];
                        if let Some(next_player) = room.players.get(next_id) {
                            if let Some(addr) = &next_player.addr {
                                let frame = json!({
                                    "type": 1,
                                    "target": "startShuffle",
                                    "arguments": [ room.deck_state ]
                                })
                                .to_string();
                                addr.do_send(ServerText(frame));
                            }
                        }
                    } else {
                        let frame = json!({
                            "type": 1,
                            "target": "shuffleComplete",
                            "arguments": [ room.deck_state ]
                        })
                        .to_string();
                        for player in room.players.values() {
                            if let Some(addr) = &player.addr {
                                addr.do_send(ServerText(frame.clone()));
                            }
                        }
                    }
                }
                Ok(evt) => {
                    println!("Unhandled event: {:?}", evt);
                }
                Err(e) => eprintln!("Bad payload: {:?}", e),
            },
            Err(e) => eprintln!("Malformed JSON: {:?}", e),
        }
    }
}

impl StreamHandler<Result<ws::Message, ws::ProtocolError>> for WsClient {
    fn handle(&mut self, msg: Result<ws::Message, ws::ProtocolError>, ctx: &mut Self::Context) {
        match msg {
            Ok(ws::Message::Ping(payload)) => ctx.pong(&payload),
            Ok(ws::Message::Text(raw)) => self.handle_text(raw.to_string(), ctx),
            Ok(ws::Message::Close(reason)) => {
                println!("Client {} disconnected: {:?}", self.id, reason);
                ctx.stop();
            }
            _ => {}
        }
    }
}
