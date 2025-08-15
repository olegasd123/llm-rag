
## Server side
It's a Web API which services the client app, makes requests to vector db and LM Studio API.
Use Python, FastAPI.

### Vector data storage
Qdrant database stores a custom data that will be inserted into a user prompt and send them to the model using LM Studio API.
Will be hosted in a Docker container

### LM studio
It's a host with the models

## Client side
It's a web app that allows a user to make prompts to the server side (Web API).
Use Next.js

### Web application
The Web app will have a similar UI just like ChatGPT has.

### User data storage
PostgreSQL database which stores user data such as credentials, chats' history, preferences, etc.
Will be hosted in a Docker container