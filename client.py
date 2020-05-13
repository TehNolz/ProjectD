import requests
import json

#Cookies = dict(SessionID="E29VtNKW8EiQ67pRzcm0qA==")

address = "http://localhost/api/login"
JSON = {
    "Email": "Administrator",
    "Password": "W@chtw00rd",
    "RememberMe": True
}
response = requests.post(address, json=JSON)#, cookies=Cookies)
print(response.status_code)
print(response.content)
print(response)

print(response.cookies)