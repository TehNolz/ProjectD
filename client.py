import requests
import json

Cookies = dict(SessionID="pzS/vN7nzk2pArY7BHx89g==")

address = "http://localhost/api/account"
JSON = {
    "Email": "TestUser@example.com",
    "Password": "W@chtw00rd",
    "RememberMe": True
}
response = requests.post(address, json=JSON, cookies=Cookies)
print(response.status_code)
print(response.content)
print(response)

print(response.cookies)