use crate::room::room::Room;
use crate::types::PlayerId;
use reqwest::blocking::Client;
use serde::Deserialize;
use serde_json::json;

#[derive(Debug, Deserialize)]
struct ExecResponse {
    ok: bool,
    code: u16,
    data: ExecData,
}

#[derive(Debug, Deserialize)]
struct ExecData {
    outputs: String, // the aggregated key
    witness: String,
}

/// Aggregate all registered public keys into one ElGamal key.
/// Returns the hex string from `outputs` on success.
pub fn aggregate_public_keys(room: &Room) -> Result<String, Box<dyn std::error::Error>> {
    // 1. collect player IDs in insertion order
    let players: Vec<PlayerId> = room.players.keys().cloned().collect();

    // 2. collect keys in the same order as players[]
    let mut pks: Vec<String> = players
        .iter()
        .filter_map(|pid| room.public_keys.get(pid))
        .cloned()
        .collect();

    let num_pks = pks.len().to_string();

    // 3. pad with "0" out to 10 entries
    pks.resize(10, "0".into());

    // 4. build payload
    let payload = json!({
        "circuit_name": "aggregatePublicKeys",
        "data": {
            "pks": pks,
            "num_pks": num_pks,
        }
    });

    // 5. POST (blocking)
    let client = Client::new();
    let resp = client
        .post("http://localhost:3000/execute")
        .json(&payload)
        .send()?
        .error_for_status()?; // fail on non-2xx

    // 6. parse
    let exec: ExecResponse = resp.json()?;
    if !exec.ok {
        Err(format!("Circuit execution failed: {:?}", exec).into())
    } else {
        Ok(exec.data.outputs)
    }
}
