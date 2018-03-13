package main

import (
	"context"
	"encoding/base64"
	"fmt"
	"io"
	"log"
)

type Dispatcher struct {
	in      io.Reader
	out     io.Writer
	clients map[int]*ManagedClient
}

func NewDispatcher(in io.Reader, out io.Writer) *Dispatcher {
	return &Dispatcher{
		in:      in,
		out:     out,
		clients: make(map[int]*ManagedClient),
	}
}

func (d *Dispatcher) Start(ctx context.Context) <-chan struct{} {
	done := make(chan struct{})
	go d.run(ctx, done)
	return done
}

func (d *Dispatcher) run(ctx context.Context, done chan<- struct{}) {
	inputMessages := d.startReadInputMessages(ctx)

	outputMessages := make(chan *Message)
	defer close(outputMessages)
	go d.writeOutputMessages(ctx, outputMessages)

	closedClients := make(chan int)
	defer close(closedClients)

Loop:
	for {
		select {
		case <-ctx.Done():
			break Loop
		case msg, ok := <-inputMessages:
			if !ok {
				break Loop
			}
			outMsg := d.getOutputMessage(msg, outputMessages, closedClients)
			if outMsg != nil {
				outputMessages <- outMsg
			}
		case clientId := <-closedClients:
			delete(d.clients, clientId)
			outputMessages <- NewMessage(TypeClientDestroyed, clientId)
		}
	}

	for _, client := range d.clients {
		client.Close()
	}

	for range d.clients {
		<-closedClients
	}

	close(done)
}

func (d *Dispatcher) getOutputMessage(msg *Message, outputMessages chan<- *Message, closedClients chan<- int) *Message {
	switch msg.Type {
	case TypeCheckPresence:
		return NewMessage(ResultSuccess, msg.Id, HelperVersion)
	case TypeCreateClient:
		clientId := randInt()
		for d.clients[clientId] != nil {
			clientId = randInt()
		}
		client := NewManagedClient(clientId, outputMessages, closedClients)
		d.clients[clientId] = client
		go client.Run()
		return NewMessage(ResultClientCreated, msg.Id, clientId)
	case TypeSetClientTickRate:
		clientId := msg.Payload[0].(int)
		tickRate := msg.Payload[1].(int)
		if client, ok := d.clients[clientId]; ok {
			client.SetTickRate(tickRate)
			return NewMessage(ResultSuccess, msg.Id)
		}
		return NewMessage(ResultError, msg.Id, fmt.Sprintf("unexisting client id: %d", clientId))
	case TypeConnectClient:
		clientId := msg.Payload[0].(int)
		connectTokenBase64 := msg.Payload[1].(string)
		connectToken, _ := base64.StdEncoding.DecodeString(connectTokenBase64)
		if client, ok := d.clients[clientId]; ok {
			client.Connect(connectToken)
			return NewMessage(ResultSuccess, msg.Id)
		}
		return NewMessage(ResultError, msg.Id, fmt.Sprintf("unexisting client id: %d", clientId))
	case TypeSendPacket:
		clientId := msg.Payload[0].(int)
		packetDataBase64 := msg.Payload[1].(string)
		packetData, _ := base64.StdEncoding.DecodeString(packetDataBase64)
		if client, ok := d.clients[clientId]; ok {
			client.SendData(packetData)
			return NewMessage(ResultSuccess, msg.Id)
		}
		return NewMessage(ResultError, msg.Id, fmt.Sprintf("unexisting client id: %d", clientId))
	case TypeGetClientState:
		clientId := msg.Payload[0].(int)
		if client, ok := d.clients[clientId]; ok {
			client.AnswerState(msg.Id)
			return nil
		}
		return NewMessage(ResultError, msg.Id, fmt.Sprintf("unexisting client id: %d", clientId))
	case TypeDestroyClient:
		clientId := msg.Payload[0].(int)
		if client, ok := d.clients[clientId]; ok {
			delete(d.clients, clientId)
			client.Close()
			return NewMessage(ResultSuccess, msg.Id)
		}
		return NewMessage(ResultError, msg.Id, fmt.Sprintf("unexisting client id: %d", clientId))
	}

	return NewMessage(ResultError, msg.Id, fmt.Sprintf("unknown message type: %d", msg.Type))
}

func (d *Dispatcher) startReadInputMessages(ctx context.Context) <-chan *Message {
	messages := make(chan *Message)
	go d.readInputMessages(ctx, messages)
	return messages
}

func (d *Dispatcher) readInputMessages(ctx context.Context, messages chan<- *Message) {
Loop:
	for {
		select {
		case <-ctx.Done():
			break Loop
		default:
			msg, err := ReadMessage(d.in)
			if err != nil {
				if err != io.EOF {
					log.Println("ReadMessage", err)
				}
				break Loop
			}
			messages <- msg
		}
	}
	close(messages)
}

func (d *Dispatcher) writeOutputMessages(ctx context.Context, messages <-chan *Message) {
Loop:
	for {
		select {
		case <-ctx.Done():
			for msg := range messages {
				d.writeOutputMessage(msg)
			}
			break Loop
		case msg, ok := <-messages:
			if !ok {
				break Loop
			}
			d.writeOutputMessage(msg)
		}
	}
}

func (d *Dispatcher) writeOutputMessage(msg *Message) {
	err := msg.WriteTo(d.out)
	if err != nil {
		log.Println("writeOutputMessage", err)
		errMsg := NewMessage(ResultErrorInternal, msg.Id, err.Error())
		errMsg.WriteTo(d.out)
	}
}
