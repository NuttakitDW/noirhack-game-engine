use futures::{SinkExt, StreamExt};
use tokio::{
    task,
    time::{timeout, Duration, Instant},
};
use tokio_tungstenite::{connect_async, tungstenite::Message};
use url::Url;

async fn spawn_server() -> (u16, task::JoinHandle<()>) {
    let port = portpicker::pick_unused_port().unwrap();
    let bind = format!("127.0.0.1:{port}");
    let server = wss_server::run_on(&bind).await.unwrap();
    let handle = task::spawn(async move {
        server.await.unwrap();
    });
    tokio::time::sleep(Duration::from_millis(200)).await;
    (port, handle)
}

#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn night_phase_flow() {
    let (port, srv) = spawn_server().await;
    let url = Url::parse(&format!("ws://127.0.0.1:{port}/ws")).unwrap();

    let mut clients = futures::future::join_all((0..4).map(|_| async {
        let (ws, _) = connect_async(url.clone()).await.unwrap();
        ws
    }))
    .await;

    for (i, sock) in clients.iter_mut().enumerate() {
        sock.send(Message::Text(format!(
            r#"{{"type":1,"target":"join","arguments":[{{"name":"P{i}"}}]}}"#
        )))
        .await
        .unwrap();
        sock.send(Message::Text(
            r#"{"type":1,"target":"ready","arguments":[true]}"#.into(),
        ))
        .await
        .unwrap();
    }

    let mut id_by_name = std::collections::HashMap::<String, String>::new();

    let mut wolf_idx: Option<usize> = None;
    let mut seer_idx: Option<usize> = None;

    while wolf_idx.is_none() || seer_idx.is_none() {
        for (idx, sock) in clients.iter_mut().enumerate() {
            if let Ok(Some(Ok(Message::Text(txt)))) =
                timeout(Duration::from_millis(100), sock.next()).await
            {
                if txt.contains(r#""target":"lobby""#) {
                    let v: serde_json::Value = serde_json::from_str(&txt).unwrap();
                    if let Some(players) = v["arguments"][0]["players"].as_array() {
                        for p in players {
                            let id = p["id"].as_str().unwrap().to_string();
                            let name = p["name"].as_str().unwrap().to_string();
                            id_by_name.insert(name, id);
                        }
                    }
                }
                if txt.contains(r#""target":"role""#) {
                    if txt.contains("Werewolf") {
                        wolf_idx = Some(idx);
                    }
                    if txt.contains("Seer") {
                        seer_idx = Some(idx);
                    }
                }
            }
        }
    }
    let wolf = wolf_idx.unwrap();
    let seer = seer_idx.unwrap();
    let victim = (0..4).find(|&i| i != wolf && i != seer).unwrap();

    let victim_uuid = id_by_name[&format!("P{victim}")].clone();
    let wolf_uuid = id_by_name[&format!("P{wolf}")].clone();

    clients[wolf]
        .send(Message::Text(
            serde_json::json!({
                "type":1,"target":"nightAction",
                "arguments":[{"action":"kill","target":victim_uuid}]
            })
            .to_string(),
        ))
        .await
        .unwrap();

    clients[seer]
        .send(Message::Text(
            serde_json::json!({
                "type":1,"target":"nightAction",
                "arguments":[{"action":"peek","target":wolf_uuid}]
            })
            .to_string(),
        ))
        .await
        .unwrap();

    let deadline = Instant::now() + Duration::from_secs(5);
    let mut saw_night_end = false;
    let mut saw_day_phase = false;

    while Instant::now() < deadline && !(saw_night_end && saw_day_phase) {
        for sock in clients.iter_mut() {
            if let Ok(Some(Ok(Message::Text(txt)))) =
                timeout(Duration::from_millis(150), sock.next()).await
            {
                if !saw_night_end && txt.contains(r#""target":"nightEnd""#) {
                    let v: serde_json::Value = serde_json::from_str(&txt).unwrap();
                    let killed = v["arguments"][0]["killed"].as_str().unwrap_or_default();
                    assert_eq!(killed, victim_uuid, "nightEnd reports correct victim");
                    saw_night_end = true;
                }

                if !saw_day_phase && txt.contains(r#""phase":"day""#) {
                    saw_day_phase = true;
                }
            }
        }
    }

    assert!(saw_night_end, "nightEnd frame was not received in time");
    assert!(saw_day_phase, "day-phase frame was not received in time");

    srv.abort();
}
