package main

import (
	"context"
	"fmt"
	"log"
	"os"
	"os/signal"
)

func main() {
	if len(os.Args) == 1 {
		err := installNetcode()
		if err != nil {
			fmt.Println("Failed to install:", err)
			os.Exit(-1)
		}
		os.Exit(0)
	}

	log.Println("netcode.io host started")

	ctx, cancel := context.WithCancel(context.Background())

	c := make(chan os.Signal, 1)
	signal.Notify(c, os.Interrupt)
	go func() {
		for range c {
			log.Println("netcode.io host gets close signal")
			cancel()
		}
	}()

	dispatcher := NewDispatcher(os.Stdin, os.Stdout)
	done := dispatcher.Start(ctx)
	<-done
}
