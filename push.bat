@echo off
echo Starting git push... > "\\mac\Home\Documents\NinjaTrader 8\bin\Custom\push_output.txt"
echo. >> "\\mac\Home\Documents\NinjaTrader 8\bin\Custom\push_output.txt"
pushd "\\mac\Home\Documents\NinjaTrader 8\bin\Custom"
echo Pushing to remote... >> push_output.txt
git push >> push_output.txt 2>&1
echo. >> push_output.txt
echo Checking status... >> push_output.txt
git status >> push_output.txt 2>&1
echo. >> push_output.txt
echo ======================================== >> push_output.txt
echo Push process completed! >> push_output.txt
echo Output saved to push_output.txt >> push_output.txt
popd
type "\\mac\Home\Documents\NinjaTrader 8\bin\Custom\push_output.txt"
pause

