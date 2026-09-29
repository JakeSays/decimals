#!/bin/bash
# Publishes dectest, the corpus and test-vector runner, to ~/bin as a native release build.
# The binary includes DecTest, Decimals.Conformance, and the three decimal types. Run this
# again after changing any of them.
set -e
dotnet publish /p/clang-build/dec/repo/DecTest/DecTest.csproj -c Release -o /home/jake/bin
