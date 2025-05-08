use crate::utils::aggregate_public_keys;
use crate::utils::verify_shuffle;
use crate::{
    room::room::{Room, SharedRoom},
    types::PlayerId,
};
use actix::AsyncContext;
use actix::{Actor, ActorContext, Handler, Message, StreamHandler};
use actix_web::rt::task;
use actix_web_actors::ws;

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
                        print!("Client::All players registered their public keys.");
                        println!("Client::Aggregating public keys...");
                        let agg_pk =
                            aggregate_public_keys(&room).expect("failed to aggregate public keys");

                        room.agg_pk = agg_pk.clone();
                        println!("Client::Public keys aggregated. agg_pk: {:?}", agg_pk);
                        println!("Client::Starting shuffle...");
                        room.initiate_shuffle();
                    }
                }
                Ok(ClientEvent::ShuffleDone {
                    encrypted_deck,
                    public_inputs,
                    proof,
                }) => {
                    println!(
                        "shuffleDone from {} → deck rows {}, inputs {}, proof head {}…",
                        self.id,
                        encrypted_deck.len(),
                        public_inputs.len(),
                        &proof[..10.min(proof.len())],
                    );

                    // clone data captured by closure
                    let room = self.room.clone();
                    let my_id = self.id.clone();
                    let inputs = public_inputs.clone();
                    let prf = proof.clone();
                    let deck = encrypted_deck.clone();

                    task::spawn_blocking(move || {
                        /* 1 – verify */
                        match verify_shuffle(&inputs, &prf) {
                            Ok(true) => {
                                println!("✔ proof valid for player {my_id}");

                                /* 2 – mutate room & choose next turn */
                                let mut room = room.lock().unwrap();

                                if room.shuffle_order.get(room.shuffle_index) != Some(&my_id) {
                                    eprintln!("shuffleDone from out-of-turn player {my_id}");
                                    return;
                                }

                                room.deck_state = deck;
                                room.shuffle_index += 1;

                                let make_frame = |target: &str, room: &Room| {
                                    serde_json::json!({
                                        "type": 1,
                                        "target": target,
                                        "arguments": [{
                                            "agg_pk": room.agg_pk,
                                            "deck":   room.deck_state
                                        }]
                                    })
                                    .to_string()
                                };

                                if room.shuffle_index < room.shuffle_order.len() {
                                    /* 3 – send startShuffle to next player only */
                                    let next_id = &room.shuffle_order[room.shuffle_index];
                                    if let Some(addr) =
                                        room.players.get(next_id).and_then(|p| p.addr.as_ref())
                                    {
                                        println!("→ startShuffle to {next_id}");
                                        addr.do_send(crate::ws::client::ServerText(make_frame(
                                            "startShuffle",
                                            &room,
                                        )));
                                    }
                                } else {
                                    /* 4 – everybody shuffled → broadcast shuffleComplete */
                                    let frame = serde_json::json!({
                                        "type": 1,
                                        "target": "shuffleComplete",
                                        "arguments": [{
                                            "deck": room.deck_state
                                        }]
                                    })
                                    .to_string();
                                    println!("✓ shuffle phase complete");
                                    for addr in
                                        room.players.values().filter_map(|p| p.addr.as_ref())
                                    {
                                        addr.do_send(crate::ws::client::ServerText(frame.clone()));
                                    }
                                }
                            }

                            Ok(false) => {
                                eprintln!("✘ proof INVALID from player {my_id}");
                                if let Some(addr) = room
                                    .lock()
                                    .unwrap()
                                    .players
                                    .get(&my_id)
                                    .and_then(|p| p.addr.as_ref())
                                {
                                    let rej = serde_json::json!({
                                        "type":1,
                                        "target":"shuffleRejected",
                                        "arguments":[{ "reason":"invalid proof" }]
                                    })
                                    .to_string();
                                    addr.do_send(crate::ws::client::ServerText(rej));
                                }
                            }

                            Err(e) => eprintln!("verify call failed for {my_id}: {e:#}"),
                        }
                    });
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
