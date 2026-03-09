#!/usr/bin/env node
"use strict";

const { spawn } = require("child_process");
const path = require("path");
const fs = require("fs");

const ext = process.platform === "win32" ? ".exe" : "";
const bin = path.join(__dirname, "..", "native", `ucp${ext}`);

if (!fs.existsSync(bin)) {
  console.error(
    "ucp binary not found. Run 'npm rebuild @mflrevan/ucp' or reinstall."
  );
  process.exit(1);
}

const child = spawn(bin, process.argv.slice(2), { stdio: "inherit" });
child.on("error", (err) => {
  console.error("Failed to start ucp:", err.message);
  process.exit(1);
});
child.on("exit", (code) => process.exit(code ?? 1));
