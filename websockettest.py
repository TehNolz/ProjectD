import requests
import websocket
import uuid
import json

def on_message(ws, message):
    print(message)
    print(len(message))

def on_close(ws):
    print("### closed ###")

def on_open(ws):
    message = json.dumps({
        "MessageID": str(uuid.uuid1()),
        "Command": "ChatMessage",
        "Data": {
            #"Name": "yeeeee",
            #"Private": False,
            "ChatroomID": "8f77e594-a58b-4a57-ba78-b55b17510ed9",
            "MessageText": "yeet skeet"
        }
    })
    ws.send(message)

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
        cookie="SessionID="+GetSessionID() #input("Username:"), input("Password:")
    )
    ws.run_forever()
