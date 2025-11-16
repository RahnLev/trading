@echo off
echo Starting git commit... > "\\mac\Home\Documents\NinjaTrader 8\bin\Custom\commit_output.txt"
echo. >> "\\mac\Home\Documents\NinjaTrader 8\bin\Custom\commit_output.txt"
echo Adding files... >> "\\mac\Home\Documents\NinjaTrader 8\bin\Custom\commit_output.txt"
pushd "\\mac\Home\Documents\NinjaTrader 8\bin\Custom"
git add AddOns\*.cs Indicators\*.cs Strategies\*.cs Shared\*.cs *.csproj *.sln .vscode\launch.json >> commit_output.txt 2>&1
echo. >> commit_output.txt
echo Committing... >> commit_output.txt
git commit -m "working on cursor PC" >> commit_output.txt 2>&1
echo. >> commit_output.txt
echo Checking status... >> commit_output.txt
git status >> commit_output.txt 2>&1
echo. >> commit_output.txt
echo Last commit: >> commit_output.txt
git log --oneline -1 >> commit_output.txt 2>&1
echo. >> commit_output.txt
echo ======================================== >> commit_output.txt
echo Commit process completed! >> commit_output.txt
echo Output saved to commit_output.txt >> commit_output.txt
popd
type "\\mac\Home\Documents\NinjaTrader 8\bin\Custom\commit_output.txt"
pause

