package main

import (
	cryptoRand "crypto/rand"
	"encoding/binary"
	mathRand "math/rand"
	"time"
)

func init() {
	mathRand.Seed(time.Now().UnixNano())
}

func randInt() int {
	buf := make([]byte, 4)
	if _, err := cryptoRand.Read(buf); err != nil {
		return mathRand.Int()
	}
	return int(binary.LittleEndian.Uint32(buf))
}
