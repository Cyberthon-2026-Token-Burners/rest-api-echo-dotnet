# EchoServer End-User Usage Guide

Welcome to **EchoServer**, a lightweight diagnostic service built with ASP.NET Core Minimal APIs. This guide describes the direct usage of the compiled service and outlines how to construct requests to explore and validate network interactions.

---

## Running the Service
To boot up the application, configure your listening port with the `PORT` environment variable and execute the binary or project:

```sh
PORT=8080 dotnet run --project src/EchoServer.Api/EchoServer.Api.csproj
```

For compiled releases, launch the executable directly:
```sh
PORT=8080 ./EchoServer.Api
```
Ensure the application binds correctly to Kestrel at `http://0.0.0.0:8080` (or your chosen port).

---

## Endpoint Interface reference

### 1. Health Status Checks
- **Route**: `GET /healthz`
- **Description**: Verifies the service is alive.
- **Response**: `HTTP 200 OK`
- **Body**: `OK` (Content-Type: `text/plain`)

### 2. Request Metadata Echo
- **Route**: `GET /echo` or `ANY /echo/metadata`
- **Description**: Compiles incoming HTTP context metrics and returns them in a clean JSON format. Query parameters containing multiple values for a single key are mapped to an array of strings.
- **Response**: `HTTP 200 OK` (Content-Type: `application/json; charset=utf-8`)
- **Example Invocation**:
  ```sh
  curl -i "http://localhost:8080/echo?tags=networking&tags=tracing&debug=true" -H "X-Custom-Client: TokenBurners"
  ```
- **Response Body Structure**:
  ```json
  {
    "method": "GET",
    "path": "/echo",
    "query": {
      "tags": ["networking", "tracing"],
      "debug": ["true"]
    },
    "headers": {
      "Accept": ["*/*"],
      "Host": ["localhost:8080"],
      "User-Agent": ["curl/8.4.0"],
      "X-Custom-Client": ["TokenBurners"]
    },
    "protocol": "HTTP/1.1",
    "scheme": "http",
    "host": "localhost:8080",
    "url": "http://localhost:8080/echo?tags=networking&tags=tracing&debug=true"
  }
  ```

### 3. Raw Body Echo
- **Route**: `ANY /echo/body` or overloaded methods `POST`, `PUT`, `PATCH`, `DELETE` on `/echo`
- **Description**: Echoes back the identical binary payload sent in the request stream.
- **Rules & Fallbacks**:
  - **Preserved Content-Type**: The outgoing response retains the exact original `Content-Type` specified on the incoming request (e.g., `application/octet-stream`, `application/json`, etc.).
  - **Empty Payload under `/echo/body`**: Returns `HTTP 204 No Content` directly.
  - **Empty Payload under overloaded `/echo` (POST/PUT/PATCH/DELETE)**: Automatically falls back to returning the JSON Metadata payload (`HTTP 200 OK` with JSON content).
  - **Size Cap (10 MB)**: If the incoming payload size exceeds exactly **10,485,760 bytes**, the request is immediately short-circuited with `HTTP 413 Payload Too Large`.
- **Example (Sending binary payload)**:
  ```sh
  curl -i -X POST \
    -H "Content-Type: application/octet-stream" \
    --data-binary @my-binary-file.bin \
    http://localhost:8080/echo/body
  ```
- **Response**:
  `HTTP 200 OK` containing the identical binary byte stream.

---

## Custom Control Headers

You can append headers to *any* request to force a specific status code response or server delay.

### `X-Echo-Status`
- **Allowed Range**: `[100, 599]`
- **Action**: Overrides the final response code. For example, to test error handling strategies, pass `X-Echo-Status: 502` to get a Bad Gateway response.

### `X-Echo-Delay-ms`
- **Allowed Range**: `[0, 30000]` (Up to 30 seconds)
- **Action**: Artificially slows down the response to test timeout handling configurations. Utilizes async, non-blocking sleeps so memory overhead remains low.

#### Order of Evaluation:
If you supply invalid headers, validation occurs instantly. Validation is performed in the following order:
1. Check `X-Echo-Status`. If outside the range, returns `HTTP 400 Bad Request` immediately.
2. Check `X-Echo-Delay-ms`. If outside the range, returns `HTTP 400 Bad Request` immediately.

---

## Error Handling Reference
- **HTTP 413 Payload Too Large**: Promptly triggered if raw payloads exceed 10 MB.
- **HTTP 400 Bad Request**: Returned if either control header contains non-integer values, or integers outside the validated ranges.