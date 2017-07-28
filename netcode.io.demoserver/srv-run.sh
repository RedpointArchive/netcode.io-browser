#!/bin/bash

set -e
set -x

cd /srv
if [ -d ../lib64 ]; then
  cp libnetcode64.so libnetcode.so
else
  cp libnetcode32.so libnetcode.so
fi

LD_LIBRARY_PATH=.:$LD_LIBRARY_PATH /usr/bin/mono netcode.io.demoserver.exe $*