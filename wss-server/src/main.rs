use uuid;
mod game;
mod message;
mod room;
mod types;
mod utils;
mod ws;

use actix_web::web::Data;
use actix_web::{web, App, Error, HttpRequest, HttpResponse, HttpServer};
use actix_web_actors::ws as actix_ws;
use room::room::{Room, SharedRoom};
use std::sync::{Arc, Mutex};
use ws::client::WsClient;

async fn ws_handler(
    req: HttpRequest,
    stream: web::Payload,
    room: Data<SharedRoom>,
) -> Result<HttpResponse, Error> {
    let id = uuid::Uuid::new_v4().to_string();
    let client = WsClient::new(id, room.get_ref().clone());
    actix_ws::start(client, &req, stream)
}

#[actix_web::main]
async fn main() -> std::io::Result<()> {
    let room: SharedRoom = Arc::new(Mutex::new(Room::new()));

    HttpServer::new(move || {
        App::new()
            .app_data(web::Data::new(room.clone()))
            .route("/ws", web::get().to(ws_handler))
    })
    .bind(("0.0.0.0", 8080))?
    .run()
    .await
}
