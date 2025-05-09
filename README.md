# 🎮 My Game Project

This project includes a downloadable game client, a WebSocket server, and an API server. Follow the steps below to set up and run everything on your local machine.

---

## 📁 Part 1: Download and Run the Game

#### 1. Download the Game ZIP  
  [Download Game (.zip)](./Noir%20WereWolf.zip)

#### 2. Extract the ZIP  
  Extract the ZIP file to a folder of your choice.

#### 3. Run the command
  open your terminal and run the command
```bash
xattr -rd com.apple.quarantine "/Users/your-machine-name/Downloads/Noir WereWolf.app"
```

## Part 2: Run the WebSocket Server

#### 1 Clone the Repository

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
cd wss-server
```

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
bun start
```

## Part 4: Set up game source

To use WebSockets in your game, the server must be hosted on a public domain and served over HTTPS. For local development, we recommend using a tool like ngrok to expose your WebSocket server with a secure wss:// URL.

#### 1. Install ngrok

```bash
bew install ngrok
```

#### 2. Signup ngrok

https://dashboard.ngrok.com/get-started/setup/macos

#### 3. Setup ngrok in your machine

You can get your token from 
https://dashboard.ngrok.com/get-started/your-authtoken

```bash 
ngrok config add-authtoken <<TOKEN>>
```

#### 4. Start ngrok server

```bash
ngrok http http://localhost:8080
```

Then we will get 

```bash
https://your-domain-name.ngrok-free.app
```

convert it to websocket server
```bash
wss//your-domain-name.ngrok-free.app/ws
```

## Final part

after we config all of the server and game engine we will start the game

![game](./game.png)

#### 1. Add your player name

#### 2. Add the websocket server from your ngrok

#### 3. Let's play the game

!!! Note all of players must to use same of websocket server
