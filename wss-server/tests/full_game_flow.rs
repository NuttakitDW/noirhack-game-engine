use futures::{SinkExt, StreamExt};
use std::collections::HashMap;
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
async fn full_game_flow() {
    let (port, srv) = spawn_server().await;
    let url = Url::parse(&format!("ws://127.0.0.1:{port}/ws")).unwrap();

    let mut clients = futures::future::join_all((0..4).map(|_| async {
        let (ws, _) = connect_async(url.clone()).await.unwrap();
        ws
    }))
    .await;

    for (i, sock) in clients.iter_mut().enumerate() {
        let join = format!(r#"{{"type":1,"target":"join","arguments":[{{"name":"P{i}"}}]}}"#);
        sock.send(Message::Text(join)).await.unwrap();
        sock.send(Message::Text(
            r#"{"type":1,"target":"ready","arguments":[true]}"#.into(),
        ))
        .await
        .unwrap();
    }

    let mut id_by_name = HashMap::new();
    let (mut wolf_idx, mut seer_idx) = (None, None);
    let mut villager_idxs = Vec::new();

    while wolf_idx.is_none() || seer_idx.is_none() || villager_idxs.len() < 2 {
        for (idx, sock) in clients.iter_mut().enumerate() {
            if let Ok(Some(Ok(Message::Text(txt)))) =
                timeout(Duration::from_millis(100), sock.next()).await
            {
                if txt.contains(r#""target":"lobby""#) {
                    let v: serde_json::Value = serde_json::from_str(&txt).unwrap();
                    for p in v["arguments"][0]["players"].as_array().unwrap() {
                        id_by_name.insert(
                            p["name"].as_str().unwrap().to_string(),
                            p["id"].as_str().unwrap().to_string(),
                        );
                    }
                }
                if txt.contains(r#""target":"role""#) {
                    if wolf_idx.is_none() && txt.contains("Werewolf") {
                        wolf_idx = Some(idx);
                    } else if seer_idx.is_none() && txt.contains("Seer") {
                        seer_idx = Some(idx);
                    } else if txt.contains("Villager") && !villager_idxs.contains(&idx) {
                        villager_idxs.push(idx);
                    }
                }
            }
        }
    }

    let wolf = wolf_idx.unwrap();
    let seer = seer_idx.unwrap();
    let villager = villager_idxs[0];
    let uuid_v = id_by_name[&format!("P{villager}")].clone();
    let uuid_w = id_by_name[&format!("P{wolf}")].clone();

    clients[wolf].send(Message::Text(
        serde_json::json!({"type":1,"target":"nightAction","arguments":[{"action":"kill","target":uuid_v}]}).to_string()
    )).await.unwrap();
    clients[seer].send(Message::Text(
        serde_json::json!({"type":1,"target":"nightAction","arguments":[{"action":"peek","target":uuid_w}]}).to_string()
    )).await.unwrap();

    let mut saw_day = false;
    let deadline1 = Instant::now() + Duration::from_secs(5);
    while Instant::now() < deadline1 && !saw_day {
        for sock in clients.iter_mut() {
            if let Ok(Some(Ok(Message::Text(txt)))) =
                timeout(Duration::from_millis(100), sock.next()).await
            {
                if txt.contains(r#""phase":"day""#) {
                    saw_day = true;
                    break;
                }
            }
        }
    }
    assert!(saw_day);

    for (i, sock) in clients.iter_mut().enumerate() {
        if i == villager {
            continue;
        }
        let vote =
            serde_json::json!({"type":1,"target":"vote","arguments":[uuid_w.clone()]}).to_string();
        sock.send(Message::Text(vote)).await.unwrap();
    }

    let mut saw_go = false;
    let mut winner = String::new();
    let deadline2 = Instant::now() + Duration::from_secs(5);
    while Instant::now() < deadline2 && !saw_go {
        for sock in clients.iter_mut() {
            if let Ok(Some(Ok(Message::Text(txt)))) =
                timeout(Duration::from_millis(100), sock.next()).await
            {
                if txt.contains(r#""target":"gameOver""#) {
                    let v: serde_json::Value = serde_json::from_str(&txt).unwrap();
                    winner = v["arguments"][0]["winner"].as_str().unwrap().to_string();
                    saw_go = true;
                    break;
                }
            }
        }
    }
    assert!(saw_go);
    assert_eq!(winner, "villagers");

    srv.abort();
}
