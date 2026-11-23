@echo off
REM Unity WebGL 빌드를 로컬 서버로 실행한다. 빌드 폴더를 인자로 줄 수 있다.
REM   run_webgl_server.bat [빌드_폴더] [포트]
cd /d "%~dp0"
start "" http://localhost:8080
python serve_webgl.py %*
pause
