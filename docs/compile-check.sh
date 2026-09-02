#!/bin/bash
# Compile the whole Trickshot runtime assembly with Roslyn outside the editor (see CLAUDE.md).
cd "C:/Users/evrik/downloads/Trickshot/Trickshot" || exit 1
SDK=$(ls -d "/c/Program Files/dotnet/sdk/"*/ | sort -V | tail -1)
CSC="${SDK}Roslyn/bincore/csc.dll"
ED="C:/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Data"
REFS=""
for f in "$ED"/Managed/UnityEngine/UnityEngine*.dll; do REFS="$REFS -r:\"$f\""; done
REFS="$REFS -r:\"$ED/NetStandard/ref/2.1.0/netstandard.dll\" -r:\"$ED/Managed/UnityEditor.dll\" -r:\"Library/ScriptAssemblies/Unity.InputSystem.dll\""
OUT=$(mktemp -d)/Trickshot.dll
eval dotnet "\"$CSC\"" -nologo -target:library -langversion:9.0 -define:UNITY_EDITOR -out:"$OUT" $REFS -recurse:Assets/Scripts/*.cs 2>&1 | grep -v "warning CS" | grep -E "error|Error" | head -40
echo "exit=${PIPESTATUS[0]} out=$OUT"
