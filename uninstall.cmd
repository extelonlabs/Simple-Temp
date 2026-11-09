@echo off
rem Starts install.ps1 as admin to remove Simple Temp. Windows will ask you to click Yes.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File','\"%~dp0install.ps1\"','-Uninstall'"
