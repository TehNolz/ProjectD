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
            "ChatroomID": "6f80a860-2f85-4fb2-b8b4-e3263b56047a",
            "MessageText": "hello"
            #"UserID": "32226f1a-789c-44b6-9196-24eff4d8c06e",
            #"AllowAccess": True
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
        cookie="SessionID="+GetSessionID( input("Username:"))#, input("Password:")
    )
    ws.run_forever()
