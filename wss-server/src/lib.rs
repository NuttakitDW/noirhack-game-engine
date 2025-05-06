pub mod game;
pub mod message;
pub mod room;
pub mod types;
mod utils;
pub mod ws;

use actix_web::{web, App, Error, HttpRequest, HttpResponse, HttpServer};
use actix_web_actors::ws as actix_ws;
use uuid::Uuid;

use room::room::{Room, SharedRoom};
use std::sync::{Arc, Mutex};
use ws::client::WsClient;

async fn ws_handler(
    req: HttpRequest,
    stream: web::Payload,
    room: web::Data<SharedRoom>,
) -> Result<HttpResponse, Error> {
    let id = Uuid::new_v4().to_string();
    let client = WsClient::new(id, room.get_ref().clone());
    actix_ws::start(client, &req, stream)
}

pub async fn run_on(bind_addr: &str) -> std::io::Result<actix_web::dev::Server> {
    let room: SharedRoom = Arc::new(Mutex::new(Room::new()));

    let server = HttpServer::new(move || {
        App::new()
            .app_data(web::Data::new(room.clone()))
            .route("/ws", web::get().to(ws_handler))
    })
    .bind(bind_addr)?
    .run();

    Ok(server)
}
