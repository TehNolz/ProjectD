import requests
import websocket
import uuid
import json

def on_message(ws, message):
    print("text:"+message)

def on_close(ws):
    print("### closed ###")

def on_open(ws):
    while(True):
        message = json.dumps({
            "MessageID": str(uuid.uuid1()),
            "Flags": 000,
            "Type": "Chat",
            "Command": "ChatHistory",
            "Data": {
                "ChatroomID": "36b573d8-ea19-4f08-9ddb-a8606af20951",
                "Start": 22,
                "Amount": 10,
            }
        })
        ws.send(message)
        input("Next")
        break

def GetSessionID(email = "Administrator", password = "W@chtw00rd"):
    address = "http://localhost/api/login"
    response = requests.post(address, json={"Email": email, "Password": password, "RememberMe": True})
    print(response.status_code)
    print("SessionID is "+response.cookies.get_dict()["SessionID"])
    return response.cookies.get_dict()["SessionID"]

if __name__ == "__main__":
    websocket.enableTrace(True)
    ws = websocket.WebSocketApp("ws://localhost/chat",
        on_message = on_message,
        on_close = on_close,
        on_open = on_open,
        cookie="SessionID="+GetSessionID(input("Username:"), input("Password:"))
    )
    ws.run_forever()