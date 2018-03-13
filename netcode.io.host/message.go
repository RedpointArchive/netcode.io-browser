package main

import (
	"encoding/binary"
	"encoding/json"
	"io"
)

const HelperVersion = "0.1.0"

const (
	TypeCreateClient       = 101
	TypeSetClientTickRate  = 102
	TypeConnectClient      = 103
	TypeSendPacket         = 104
	TypeReceivePacket      = 105
	TypeGetClientState     = 106
	TypeDestroyClient      = 107
	TypeClientDestroyed    = 108
	TypeCheckPresence      = 109
	TypeClientStateChanged = 110
)

const (
	ResultClientCreated = 201
	ResultSuccess       = 202
	ResultError         = 203
	ResultErrorInternal = 204
)

type Message struct {
	Type    int
	Id      int
	Payload []interface{}
}

func NewMessage(msgType, msgId int, payload ...interface{}) *Message {
	return &Message{msgType, msgId, payload}
}

func (msg *Message) WriteTo(writer io.Writer) error {
	messageArray := make([]interface{}, 2+len(msg.Payload))
	messageArray[0] = msg.Type
	messageArray[1] = msg.Id
	copy(messageArray[2:], msg.Payload)
	messageBytes, err := json.Marshal(messageArray)
	if err != nil {
		return err
	}
	err = binary.Write(writer, binary.LittleEndian, int32(len(messageBytes)))
	if err != nil {
		return err
	}
	_, err = writer.Write(messageBytes)
	return err
}

func ReadMessage(reader io.Reader) (*Message, error) {
	var size int32
	err := binary.Read(reader, binary.LittleEndian, &size)
	if err != nil {
		return nil, err
	}

	bytes := make([]byte, size)
	_, err = io.ReadFull(reader, bytes)
	if err != nil {
		return nil, err
	}
	var messageArray []interface{}
	err = json.Unmarshal(bytes, &messageArray)
	if err != nil {
		return nil, err
	}
	for i, item := range messageArray {
		if floatValue, ok := item.(float64); ok {
			messageArray[i] = int(floatValue)
		}
	}
	return NewMessage(messageArray[0].(int), messageArray[1].(int), messageArray[2:]...), nil
}
