# 🎮 My Game Project

This project includes a downloadable game client, a WebSocket server, and an API server. Follow the steps below to set everything up and run it locally on your machine.

---

## 📁 Part 1: Download and Run the Game

#### 1. Download the Game ZIP

[Download Game (.zip)](./Noir%20Werewolf.zip)

#### 2. Extract the ZIP

Unzip the file to a folder of your choice.

#### 3. Allow the app to run (macOS only)

Open your terminal and run:

```bash
xattr  -rd  com.apple.quarantine  "/Users/your-machine-name/Downloads/Noir WereWolf.app"
```

## 🔌 Part 2: Run the WebSocket Server

#### 1. Clone the Repository

```bash
git clone https://github.com/NuttakitDW/noirhack-game-engine.git
```

#### 2. Install Rust

If Rust isn’t installed, run:

```bash
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
```

#### 3. Start the WebSocket Server

```bash
cd wss-server
cargo run --bin wss-server
```

## 🌐 Part 3: Run the API Server (Bun + Noir Circuits)

#### 1. Clone the Repository

```bash
git clone https://github.com/NuttakitDW/noirhack-backend.git
```

#### 2. Install bun

If Bun isn’t installed, run:

```bash
curl -fsSL https://bun.sh/install | bash
```

#### 3. Install Dependencies

```bash
bun install
```

#### 4. Start the API Server

```bash
bun start
```

## 🔧 Part 4: Set Up WebSocket Access via ngrok

To use WebSockets in your game, the server must be accessible via a public HTTPS domain. For local development, use [ngrok](https://ngrok.com/) to expose your WebSocket server securely.

#### 1. Install ngrok

```bash
brew install ngrok
```

#### 2. Sign up for ngrok

Visit:  
[https://dashboard.ngrok.com/get-started/setup/macos](https://dashboard.ngrok.com/get-started/setup/macos)

#### 3. Configure ngrok with your token

Get your auth token from:  
[https://dashboard.ngrok.com/get-started/your-authtoken](https://dashboard.ngrok.com/get-started/your-authtoken)

Then run:

```bash
ngrok config add-authtoken <YOUR_TOKEN>
```

#### 4. Start the ngrok tunnel

```bash
ngrok http http://localhost:8080
```

You’ll get a URL like:

```bash
https://your-domain-name.ngrok-free.app
```

#### 5. Convert to WebSocket URL

```bash
wss://your-domain-name.ngrok-free.app/ws
```

## Final part

After configuring the server and game engine, you’re ready to start the game.

![game](./game.png)

#### 1. Enter your player name

#### 2. Paste the WebSocket server URL from your ngrok setup

#### 3. Start the game and enjoy!

> ⚠️ Note: All players must use the same WebSocket server to connect.

## Important Notes

Due to time constraints, not all ZK circuits have been fully implemented or integrated into the Unity demo. The **shuffle phase**, **role reveal**, **kill**, and **peek** actions are functional, but features like **announcing the role of the dead to the server** and **voting** are still missing.  
As a result, the current demo **cannot proceed through the night phase**.

Additionally, the game requires exactly **4 players** to start.  
A running **WebSocket (wss) server** is also **absolutely required** for the demo to function.
