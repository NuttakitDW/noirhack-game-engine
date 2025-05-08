use crate::utils::aggregate_public_keys;
use crate::utils::verify_shuffle;
use crate::{
    room::room::{DecryptCtx, Room, SharedRoom},
    types::PlayerId,
};
use actix::AsyncContext;
use actix::{Actor, ActorContext, Handler, Message, StreamHandler};
use actix_web::rt::task;
use actix_web_actors::ws;
use std::collections::VecDeque;

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

fn send_need_decrypt(room: &Room, requester: &PlayerId, helper: &PlayerId) {
    if let Some(addr) = room.players.get(helper).and_then(|p| p.addr.as_ref()) {
        if let Some(ctx) = room.decrypt_ctx.get(requester) {
            let frame = serde_json::json!({
            "type": 1,
            "target": "needDecrypt",
            "arguments": [{
               "for":   requester,
               "card":  ctx.card_index,
               "cipher": ctx.current_cipher
            }]
            })
            .to_string();

            addr.do_send(crate::ws::client::ServerText(frame));
            println!("→ needDecrypt to {} for {}", helper, requester);
        }
    }
}

fn send_partial_ready(room: &Room, requester: &PlayerId, partial: &[String; 2], component: &str) {
    if let Some(addr) = room.players.get(requester).and_then(|p| p.addr.as_ref()) {
        let frame = serde_json::json!({
        "type": 1,
        "target": "partialReady",
        "arguments": [{
        "card":  room.decrypt_ctx[requester].card_index,
        "partial":  partial,
        "component": component
        }]
        })
        .to_string();
        addr.do_send(crate::ws::client::ServerText(frame));
    }
}

fn send_all_parts_ready(room: &Room, requester: &PlayerId) {
    if let Some(addr) = room.players.get(requester).and_then(|p| p.addr.as_ref()) {
        let frame = serde_json::json!({
        "type": 1,
        "target": "allPartsReady",
        "arguments": [{
        "card": room.decrypt_ctx[requester].card_index
        }]
        })
        .to_string();
        addr.do_send(crate::ws::client::ServerText(frame));
    }
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

                    let room = self.room.clone();
                    let my_id = self.id.clone();
                    let inputs = public_inputs.clone();
                    let prf = proof.clone();
                    let deck = encrypted_deck.clone();

                    task::spawn_blocking(move || match verify_shuffle(&inputs, &prf) {
                        Ok(true) => {
                            println!("✔ proof valid for player {my_id}");

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
                                let frame = serde_json::json!({
                                    "type": 1,
                                    "target": "shuffleComplete",
                                    "arguments": [{
                                        "deck": room.deck_state
                                    }]
                                })
                                .to_string();
                                println!("✓ shuffle phase complete");
                                for addr in room.players.values().filter_map(|p| p.addr.as_ref()) {
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
                    });
                }
                Ok(ClientEvent::PickCard { card }) => {
                    println!("Player {} is attempting to pick card {}", self.id, card);
                    let mut room = self.room.lock().unwrap();

                    if room.taken_cards.values().any(|&c| c == card) {
                        let deny = serde_json::json!({
                            "type":1,
                            "target":"cardTaken",
                            "arguments":[{ "status":"denied", "card": card }]
                        })
                        .to_string();
                        ctx.text(deny);
                    } else {
                        room.taken_cards.insert(self.id.clone(), card);

                        let ok = serde_json::json!({
                            "type":1,
                            "target":"cardTaken",
                            "arguments":[{ "status":"ok", "card": card }]
                        })
                        .to_string();

                        ctx.text(ok.clone());
                        for p in room.players.values().filter_map(|pl| pl.addr.as_ref()) {
                            p.do_send(crate::ws::client::ServerText(ok.clone()));
                        }
                        if room.taken_cards.len() == 4 {
                            println!("All cards claimed – setting up decrypt queues");

                            let taken_cards: Vec<(PlayerId, usize)> = room
                                .taken_cards
                                .iter()
                                .map(|(player_id, &idx)| (player_id.clone(), idx))
                                .collect();

                            for (player_id, idx) in taken_cards {
                                let helpers: VecDeque<PlayerId> = room
                                    .shuffle_order
                                    .iter()
                                    .filter(|pid| *pid != &player_id)
                                    .cloned()
                                    .collect();

                                let cipher = room.deck_state[idx].clone();

                                room.decrypt_ctx.insert(
                                    player_id.clone(),
                                    DecryptCtx {
                                        helpers,
                                        current_cipher: cipher,
                                        components: Vec::new(),
                                        card_index: idx,
                                    },
                                );
                            }

                            for (player_id, ctx) in &room.decrypt_ctx {
                                if let Some(first_helper) = ctx.helpers.front() {
                                    send_need_decrypt(&room, player_id, first_helper);
                                }
                            }
                        }
                    }
                }
                Ok(ClientEvent::DecryptCard {
                    for_player,
                    card,
                    partial,
                    component,
                }) => {
                    let mut room = self.room.lock().unwrap();

                    if let Some(ctx) = room.decrypt_ctx.get_mut(&for_player) {
                        match ctx.helpers.front() {
                            Some(expected) if expected == &self.id => {}
                            _ => {
                                eprintln!("decryptCard out-of-order from {}", self.id);
                                return;
                            }
                        }

                        ctx.components.push(component.clone());
                        ctx.current_cipher = partial.clone();
                        ctx.helpers.pop_front();

                        if let Some(next_helper) = ctx.helpers.front() {
                            let next_helper_clone = next_helper.clone();
                            let for_player_clone = for_player.clone();
                            drop(room);
                            send_need_decrypt(
                                &self.room.lock().unwrap(),
                                &for_player_clone,
                                &next_helper_clone,
                            );
                        } else {
                            send_partial_ready(&room, &for_player, &partial, &component);
                            send_all_parts_ready(&room, &for_player);
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
