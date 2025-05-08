# 🎮 My Game Project

This project includes a downloadable game client, a WebSocket server, and an API server. Follow the steps below to set up and run everything on your local machine.

---

## 📁 Part 1: Download and Run the Game

1. **Download the Game ZIP**  
  👉 [Download Game (.zip)](./path-to-your-game.zip)

2. **Extract the ZIP**  
  Extract the ZIP file to a folder of your choice.

3. **Run the Game**  
  Open the extracted folder and run the game executable (or follow instructions inside the folder).

---

## Part 2: Run the WebSocket Server

```bash
git clone https://github.com/NuttakitDW/noirhack-game-engine.git
```

#### 2. Install rust
If you don't have Rust installed on your machine, you can install it by running the following command:

```bash
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
```

#### 3. Start server
  ```bash
  cargo run --bin wss-server
  ```

  
## 🌐 Part 3: Run the API Server

Clone and Run the Bun Server with Noir Circuits. 
Follow the steps below to clone and set up your Bun-based server that uses Noir circuits:

#### 1. Clone the Repository 

```bash
git clone https://github.com/NuttakitDW/noirhack-backend.git
```

#### 2. Install bun
If you don't have Bun installed on your machine, you can install it by running the following command:

```bash
curl -fsSL https://bun.sh/install | bash
```

#### 3. Install package

```bash
bun install
```

#### 4. Start server

```bash
npm start
```