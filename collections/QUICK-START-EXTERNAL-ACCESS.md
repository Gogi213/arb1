# Quick Start: Доступ с внешнего IP

## 🚀 Быстрый старт (3 шага)

### 1. Запусти сервер

```bash
cd collections/src/SpreadAggregator.Presentation
dotnet run
```

Увидишь:
```
Now listening on: http://0.0.0.0:5000
Application started.
```

---

### 2. Узнай IP сервера

**Windows:**
```bash
ipconfig
```

Найди строку:
```
IPv4 Address. . . . . . . . . . . : 192.168.1.100
```

**Linux:**
```bash
hostname -I
```

---

### 3. Открой браузер

**На ЛЮБОМ компьютере в сети:**
```
http://192.168.1.100:5000/index.html
```

**Замени `192.168.1.100` на свой IP!**

---

## ✅ Проверка работы

### Health Check

```bash
curl http://192.168.1.100:5000/health
```

**Ответ:**
```json
{
  "status": "healthy",
  "uptime": { ... },
  "memory": { ... },
  "services": {
    "exchanges": {
      "Binance": "running",
      "Bybit": "running"
    }
  }
}
```

---

### Ping

```bash
curl http://192.168.1.100:5000/ping
```

**Ответ:**
```json
{
  "status": "alive",
  "timestamp": "2025-11-19T12:34:56Z"
}
```

---

## 🔥 Firewall (Windows)

**Если не открывается - разреши порты:**

```powershell
# Открыть PowerShell от администратора
netsh advfirewall firewall add rule name="SpreadAggregator HTTP" dir=in action=allow protocol=TCP localport=5000
netsh advfirewall firewall add rule name="SpreadAggregator WebSocket" dir=in action=allow protocol=TCP localport=8181
```

---

## 🌐 Доступные endpoints

| Endpoint | URL | Описание |
|----------|-----|----------|
| Графики | `http://YOUR_IP:5000/index.html` | Web UI с графиками |
| Health | `http://YOUR_IP:5000/health` | Метрики системы |
| Ping | `http://YOUR_IP:5000/ping` | Простая проверка |
| WebSocket | `ws://YOUR_IP:8181` | Realtime данные |

---

## 📱 Мобильный доступ

**Да, можно открыть с телефона!**

1. Подключи телефон к той же WiFi сети
2. Открой браузер на телефоне
3. Введи: `http://192.168.1.100:5000/index.html`

---

## 🛠️ Troubleshooting

### Проблема: "Connection refused"

**Решение:**
1. Проверь что сервер запущен: `netstat -ano | findstr :5000`
2. Проверь firewall (см. выше)
3. Проверь IP: `ipconfig`

### Проблема: "No route to host"

**Решение:**
1. Проверь что компьютер в той же сети
2. Пинг сервера: `ping 192.168.1.100`
3. Проверь роутер/firewall

---

## 🔒 Production Security

**Для production сервера добавь:**
- HTTPS вместо HTTP
- API Key аутентификацию
- Rate limiting
- IP whitelist

**См. полную инструкцию:** `NETWORK-ACCESS.md`

---

## ✨ That's it!

**Теперь графики доступны с любого компьютера/телефона в сети!** 📊
