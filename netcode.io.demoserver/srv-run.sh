#!/bin/bash

set -e
set -x

cd /srv
LD_LIBRARY_PATH=.:$LD_LIBRARY_PATH /usr/bin/mono netcode.io.demoserver.exe $*