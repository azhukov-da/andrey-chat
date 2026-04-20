@echo off
docker-compose up -d --build
timeout /t 5 /nobreak >nul
start "" http://localhost:8080