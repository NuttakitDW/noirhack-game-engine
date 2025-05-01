//! src/main.rs
use actix_web::{web, App, Error, HttpRequest, HttpResponse, HttpServer};
use actix_web_actors::ws as actix_ws;
use uuid;
mod game;
mod types;
mod ws;

/// WebSocket upgrade handler: every new connection gets a fresh `WsClient`
/// with a unique player-id (UUID v4).
async fn ws_handler(req: HttpRequest, stream: web::Payload) -> Result<HttpResponse, Error> {
    let id = uuid::Uuid::new_v4().to_string();
    let client = ws::client::WsClient::new(id);
    actix_ws::start(client, &req, stream)
}

#[actix_web::main]
async fn main() -> std::io::Result<()> {
    // Bind on all interfaces so friends on LAN can connect.
    HttpServer::new(|| App::new().route("/ws", web::get().to(ws_handler)))
        .bind(("0.0.0.0", 8080))?
        .run()
        .await
}
