# Werewolf Game WebSocket Server

## Running the Server

1. Build and run the binary:
   ```bash
   cargo run --bin wss-server
   ```
2. By default it listens on `127.0.0.1:8080`.  
3. Connect WebSocket clients to `ws://127.0.0.1:8080/ws`.

## Features

- **Lobby**  
  • Players send `{"type":1,"target":"join","arguments":[{"name":"YourName"}]}` to join  
  • Toggle ready with `{"type":1,"target":"ready","arguments":[true]}`  
  • When 4 players are ready, game auto-starts

- **Role Assignment**  
  • One Werewolf, one Seer, two Villagers  
  • Private `"role"` frame sent to each client  

- **Night Phase**  
  • Werewolf: `{"type":1,"target":"nightAction","arguments":[{"action":"kill","target":"<PlayerID>"}]}`  
  • Seer:     `{"type":1,"target":"nightAction","arguments":[{"action":"peek","target":"<PlayerID>"}]}`  
  • Server broadcasts `nightEnd` with the killed ID and flips to Day

- **Day Phase & Voting**  
  • Clients send `{"type":1,"target":"vote","arguments":["<PlayerID>"]}`  
  • Server broadcasts `voteUpdate` after each vote  
  • When all living players have voted, server broadcasts `dayEnd` with lynched ID (or `null` on tie) and flips back to Night

- **Win Detection & Game Over**  
  • After each kill or lynch, server checks for win condition  
  • If one side is eliminated, server broadcasts  
    ```json
    {
      "type":1,
      "target":"gameOver",
      "arguments":[{
        "winner":"villagers"|"werewolves",
        "roles": { "<PlayerID>":"Role", … }
      }]
    }
    ```

- **Chat (Day-only)**  
  • Alive players during Day send `{"type":1,"target":"chat","arguments":[{"text":"…"}]}`  
  • Server broadcasts to all alive clients

## Protocol Summary

All messages are JSON frames with these fields:

- `type`: always `1`  
- `target`: one of `join`, `ready`, `role`, `gameStart`, `phase`, `nightAction`, `peekResult`, `nightEnd`, `vote`, `voteUpdate`, `dayEnd`, `gameOver`, `chat`  
- `arguments`: array of payload objects or values

## Testing

- Unit tests in `src/room/room.rs` cover night, day voting, win logic, and chat validation.  
- Integration tests in `tests/` drive end-to-end flows (`night_phase.rs`, `full_game_flow.rs`, `chat_flow.rs`).  
- Run all tests with:
  ```bash
  cargo test
  ```