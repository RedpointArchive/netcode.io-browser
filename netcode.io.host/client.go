package main

import (
	"encoding/base64"
	"github.com/wirepair/netcode"
	"log"
	"time"
)

type ManagedClient struct {
	clientId          int
	outputMessages    chan<- *Message
	closedClients     chan<- int
	connectTokens     chan []byte
	tickRates         chan int
	done              chan bool
	outgoingPackets   chan []byte
	needToAnswerState chan int
}

func NewManagedClient(clientId int, outputMessages chan<- *Message, closedClients chan<- int) *ManagedClient {
	return &ManagedClient{
		clientId:          clientId,
		outputMessages:    outputMessages,
		closedClients:     closedClients,
		connectTokens:     make(chan []byte),
		tickRates:         make(chan int),
		done:              make(chan bool),
		outgoingPackets:   make(chan []byte),
		needToAnswerState: make(chan int),
	}
}

func (client *ManagedClient) Connect(token []byte) {
	client.connectTokens <- token
}

func (client *ManagedClient) SetTickRate(tickRate int) {
	client.tickRates <- tickRate
}

func (client *ManagedClient) SendData(packet []byte) {
	client.outgoingPackets <- packet
}

func (client *ManagedClient) AnswerState(messageId int) {
	client.needToAnswerState <- messageId
}

func (client *ManagedClient) Close() {
	client.done <- true
}

func (client *ManagedClient) Run() {
	var netClient *netcode.Client
	tickRate := 60
	currentTime := 0.0
	currentTickRate := tickRate
	var currentState netcode.ClientState
	var pendingPackets [][]byte
	ticker := time.NewTicker(time.Second / time.Duration(currentTickRate))

Loop:
	for {
		select {
		case <-client.done:
			if netClient != nil {
				netClient.Close()
				netClient = nil
			}
			break Loop
		case tokenBytes := <-client.connectTokens:
			if netClient == nil {
				token, err := netcode.ReadConnectToken(tokenBytes)
				if err != nil {
					log.Println("ReadConnectToken", err)
				} else {
					netClient = netcode.NewClient(token)
					currentState = netClient.GetState()
					err = netClient.Connect()
					if err != nil {
						log.Println("connect", err)
					}
				}
			}
		case tickRate = <-client.tickRates:
		case packet := <-client.outgoingPackets:
			pendingPackets = append(pendingPackets, packet)
		case messageId := <-client.needToAnswerState:
			client.outputMessages <- NewMessage(ResultSuccess, messageId, client.getStateString(netClient))
		case <-ticker.C:
			if currentTickRate != tickRate {
				currentTickRate = tickRate
				ticker.Stop()
				ticker = time.NewTicker(time.Second / time.Duration(currentTickRate))
			}

			if netClient != nil {
				netClient.Update(currentTime)

				if currentState != netClient.GetState() {
					currentState = netClient.GetState()
					client.outputMessages <- NewMessage(TypeClientStateChanged, client.clientId, client.getStateString(netClient))
				}

				if netClient.GetState() == netcode.StateConnected {
					for _, packet := range pendingPackets {
						err := netClient.SendData(packet)
						if err != nil {
							log.Println("SendData", err)
						}
					}
					pendingPackets = pendingPackets[:0]
				}

				for {
					packet, _ := netClient.RecvData()
					if packet == nil {
						break
					}
					client.outputMessages <- NewMessage(TypeReceivePacket, client.clientId, base64.StdEncoding.EncodeToString(packet))
				}

				deltaTime := 1.0 / float64(currentTickRate)
				currentTime += deltaTime
			}
		}
	}

	close(client.connectTokens)
	close(client.tickRates)
	close(client.done)
	close(client.outgoingPackets)
	close(client.needToAnswerState)
	ticker.Stop()

	client.closedClients <- client.clientId
}

func (client *ManagedClient) getStateString(netClient *netcode.Client) string {
	if netClient == nil {
		return "disconnected"
	}

	switch netClient.GetState() {
	case netcode.StateConnected:
		return "connected"
	case netcode.StateConnectionDenied:
		return "connectionDenied"
	case netcode.StateConnectionRequestTimedOut:
		return "connectionRequestTimeout"
	case netcode.StateConnectionResponseTimedOut:
		return "connectionResponseTimeout"
	case netcode.StateConnectionTimedOut:
		return "connectionTimedOut"
	case netcode.StateTokenExpired:
		return "connectTokenExpired"
	case netcode.StateDisconnected:
		return "disconnected"
	case netcode.StateInvalidConnectToken:
		return "invalidConnectToken"
	case netcode.StateSendingConnectionRequest:
		return "sendingConnectionRequest"
	case netcode.StateSendingConnectionResponse:
		return "sendingConnectionResponse"
	}
	return "unknown"
}
