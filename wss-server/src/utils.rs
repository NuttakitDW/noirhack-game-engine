use crate::room::room::Room;
use crate::types::PlayerId;
use serde::Deserialize;
use serde_json::json;
use ureq;

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

#[derive(Debug, Deserialize)]
struct VerifyResponse {
    ok: bool,
    code: u16,
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

    // Send the request and read the body as a String
    let resp = ureq::post("http://localhost:3000/execute")
        .set("Content-Type", "application/json")
        .send_string(&payload.to_string())?;
    let body = resp.into_string()?;

    // Parse the JSON response
    let exec: ExecResponse = serde_json::from_str(&body)?;
    if !exec.ok {
        Err(format!("Circuit execution failed: {:?}", exec).into())
    } else {
        Ok(exec.data.outputs)
    }
}

pub fn verify_shuffle(
    public_inputs: &[String],
    proof: &str,
) -> Result<bool, Box<dyn std::error::Error>> {
    // 1) build the JSON payload
    let payload = json!({
        "circuit_name": "shuffle4",
        "data": {
            "public_inputs": public_inputs,
            "proof": proof
        }
    });

    // Send the request and read the body as a String
    let resp = ureq::post("http://localhost:3000/verify")
        .set("Content-Type", "application/json")
        .send_string(&payload.to_string())?;
    let body = resp.into_string()?;

    // Parse the JSON response
    let v: VerifyResponse = serde_json::from_str(&body)?;
    Ok(v.ok)
}
