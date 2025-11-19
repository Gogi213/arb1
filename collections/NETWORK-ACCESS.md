# Network Access Configuration

## Доступ с внешнего IP

Система настроена для доступа с любого IP-адреса (не только localhost).

### Endpoints

**HTTP API (ASP.NET Core):**
```
http://0.0.0.0:5000
```

**WebSocket (Fleck):**
```
ws://0.0.0.0:8181
```

---

## Как подключиться

### 1. С локальной машины (localhost)

**HTTP API:**
```bash
# Health check
curl http://localhost:5000/health

# Ping
curl http://localhost:5000/ping
```

**Графики (браузер):**
```
http://localhost:5000/index.html
```

**WebSocket (JS):**
```javascript
const ws = new WebSocket('ws://localhost:8181');
```

---

### 2. С внешнего IP (LAN/WAN)

**Найди IP сервера:**
```bash
# Windows
ipconfig

# Linux
ip addr show
```

**HTTP API:**
```bash
# Health check
curl http://YOUR_SERVER_IP:5000/health

# Пример
curl http://192.168.1.100:5000/health
```

**Графики (браузер):**
```
http://YOUR_SERVER_IP:5000/index.html

# Пример
http://192.168.1.100:5000/index.html
```

**WebSocket (JS):**
```javascript
const ws = new WebSocket('ws://YOUR_SERVER_IP:8181');

// Пример
const ws = new WebSocket('ws://192.168.1.100:8181');
```

---

## Firewall Configuration

### Windows Firewall

**Разрешить порты:**
```powershell
# HTTP API (5000)
netsh advfirewall firewall add rule name="SpreadAggregator HTTP" dir=in action=allow protocol=TCP localport=5000

# WebSocket (8181)
netsh advfirewall firewall add rule name="SpreadAggregator WebSocket" dir=in action=allow protocol=TCP localport=8181
```

**Проверить правила:**
```powershell
netsh advfirewall firewall show rule name="SpreadAggregator HTTP"
netsh advfirewall firewall show rule name="SpreadAggregator WebSocket"
```

**Удалить правила (если нужно):**
```powershell
netsh advfirewall firewall delete rule name="SpreadAggregator HTTP"
netsh advfirewall firewall delete rule name="SpreadAggregator WebSocket"
```

---

### Linux Firewall (ufw)

```bash
# Разрешить порты
sudo ufw allow 5000/tcp
sudo ufw allow 8181/tcp

# Проверить статус
sudo ufw status
```

---

### Linux Firewall (firewalld)

```bash
# Разрешить порты
sudo firewall-cmd --permanent --add-port=5000/tcp
sudo firewall-cmd --permanent --add-port=8181/tcp
sudo firewall-cmd --reload

# Проверить
sudo firewall-cmd --list-ports
```

---

## Cloud/VPS Configuration

### AWS EC2

**Security Group Rules:**
- Type: Custom TCP
- Port: 5000
- Source: 0.0.0.0/0 (или ваш IP)

- Type: Custom TCP
- Port: 8181
- Source: 0.0.0.0/0 (или ваш IP)

### Azure VM

**Network Security Group (NSG):**
- Inbound rule: TCP 5000, Source: Any
- Inbound rule: TCP 8181, Source: Any

### Google Cloud

**Firewall Rules:**
```bash
gcloud compute firewall-rules create allow-spread-aggregator \
  --allow tcp:5000,tcp:8181 \
  --source-ranges 0.0.0.0/0
```

---

## CORS Configuration

CORS уже настроен для любых origins:

```csharp
// Program.cs
policy.AllowAnyOrigin()
      .AllowAnyMethod()
      .AllowAnyHeader();
```

Можно подключаться с любого домена/IP.

---

## Security Considerations

### Production Recommendations

1. **Ограничить доступ по IP:**
   ```json
   // appsettings.Production.json
   {
     "AllowedHosts": "*.yourdomain.com"
   }
   ```

2. **Добавить API Key:**
   ```csharp
   // Middleware для проверки API key в заголовках
   ```

3. **HTTPS вместо HTTP:**
   ```json
   {
     "Kestrel": {
       "Endpoints": {
         "Https": {
           "Url": "https://0.0.0.0:5001",
           "Certificate": {
             "Path": "cert.pfx",
             "Password": "your_password"
           }
         }
       }
     }
   }
   ```

4. **WSS вместо WS:**
   ```json
   {
     "ConnectionStrings": {
       "WebSocket": "wss://0.0.0.0:8181"
     }
   }
   ```

5. **Rate Limiting:**
   ```csharp
   // Добавить rate limiting middleware
   ```

---

## Troubleshooting

### Проблема: "Connection refused"

**Проверь что сервер запущен:**
```bash
# Windows
netstat -ano | findstr :5000
netstat -ano | findstr :8181

# Linux
netstat -tulpn | grep :5000
netstat -tulpn | grep :8181
```

**Проверь firewall:**
```bash
# Windows
netsh advfirewall show allprofiles

# Linux
sudo ufw status
sudo firewall-cmd --list-all
```

---

### Проблема: "No route to host"

**Проверь IP адрес:**
```bash
# Windows
ipconfig

# Linux
ip addr show
```

**Проверь что клиент в той же сети:**
```bash
ping YOUR_SERVER_IP
```

---

### Проблема: WebSocket не подключается

**Проверь что WebSocket сервер запущен:**
```bash
curl http://YOUR_SERVER_IP:5000/health
```

**Проверь CORS настройки:**
- WebSocket может блокироваться браузером из-за CORS
- Проверь консоль браузера (F12)

**Проверь протокол:**
- `ws://` для HTTP
- `wss://` для HTTPS

---

## Testing

### Test HTTP API

```bash
# Локально
curl http://localhost:5000/health

# Внешний IP
curl http://YOUR_SERVER_IP:5000/health
```

### Test WebSocket

**JavaScript (браузер):**
```javascript
const ws = new WebSocket('ws://YOUR_SERVER_IP:8181');

ws.onopen = () => console.log('Connected!');
ws.onmessage = (event) => console.log('Message:', event.data);
ws.onerror = (error) => console.error('Error:', error);
ws.onclose = () => console.log('Disconnected');
```

**wscat (CLI tool):**
```bash
npm install -g wscat
wscat -c ws://YOUR_SERVER_IP:8181
```

---

## Configuration Files

### appsettings.json

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:5000"
      }
    }
  },
  "ConnectionStrings": {
    "WebSocket": "ws://0.0.0.0:8181"
  }
}
```

**0.0.0.0** означает "слушать на всех сетевых интерфейсах".

**Альтернативы:**
- `127.0.0.1` - только localhost
- `0.0.0.0` - все интерфейсы (LAN + WAN)
- `192.168.1.100` - конкретный IP интерфейс

---

## Quick Start

**1. Запусти сервер:**
```bash
cd collections/src/SpreadAggregator.Presentation
dotnet run
```

**2. Узнай свой IP:**
```bash
ipconfig  # Windows
ip addr   # Linux
```

**3. Открой браузер на ДРУГОМ компьютере:**
```
http://YOUR_SERVER_IP:5000/index.html
```

**4. Наслаждайся графиками!** 📊
