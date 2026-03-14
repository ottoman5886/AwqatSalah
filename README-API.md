# AwqatSalah API

Eine selbst gehostete REST API für Diyanet Gebetszeiten, basierend auf dem [DinIsleriYuksekKurulu/AwqatSalah](https://github.com/DinIsleriYuksekKurulu/AwqatSalah) Projekt.

## Features

- 🕌 Gebetszeiten von Diyanet (awqatsalah.diyanet.gov.tr)
- 🔑 API Key Authentifizierung (X-API-Key Header)
- 💾 Persistenter Jahres-Cache via Turso (SQLite Cloud)
- 🔄 Automatische Fallback-Kette (Yearly → Monthly → Weekly → Stale)
- 🛡️ Rate Limiting (50 Anfragen/Minute pro Key)
- 📊 Health Check Endpunkt
- 📖 Swagger UI

---

## Endpunkte

### Gebetszeiten

| Methode | Endpunkt | Beschreibung |
|---------|----------|--------------|
| GET | `/api/AwqatSalah/Daily/{cityId}` | Heutige Gebetszeiten |
| GET | `/api/AwqatSalah/Weekly/{cityId}` | Nächste 7 Tage |
| GET | `/api/AwqatSalah/Monthly/{cityId}` | Nächste 30 Tage |
| POST | `/api/AwqatSalah/Yearly` | Ganzes Jahr (aus Cache) |
| GET | `/api/AwqatSalah/Eid/{cityId}` | Bayram Zeiten |
| GET | `/api/AwqatSalah/Ramadan/{cityId}` | Ramadan Imsakiye |
| POST | `/api/AwqatSalah/WarmCache/{cityId}` | Jahres-Cache aufbauen (Admin) |

### Orte

| Methode | Endpunkt | Beschreibung |
|---------|----------|--------------|
| GET | `/api/v2/Place/Countries` | Alle Länder |
| GET | `/api/v2/Place/States/{countryId}` | Regionen nach Land |
| GET | `/api/v2/Place/Cities/{stateId}` | Städte nach Region |

### Sonstiges

| Methode | Endpunkt | Beschreibung |
|---------|----------|--------------|
| GET | `/api/DailyContent` | Tagesvers, Hadith, Dua |
| GET | `/health` | Health Check |

---

## Authentifizierung

Alle Endpunkte (außer `/health`) benötigen einen API Key im Header:

```
X-API-Key: dein-api-key
```

### Admin Endpunkte

`WarmCache` und `?refresh=true` benötigen einen Admin Key:

```
POST /api/AwqatSalah/WarmCache/10102
X-API-Key: dein-admin-key
```

---

## Cache Strategie

```
Erste Anfrage für eine Stadt:
  → Fallback-Kette:
    1. Jahres-Cache in Turso vorhanden? → zurückgeben
    2. Monats-Cache vorhanden?          → zurückgeben
    3. Monthly bei Diyanet laden        → cachen
    4. Weekly bei Diyanet laden         → cachen
    5. Stale Cache                      → zurückgeben (veraltet)

WarmCache (Admin):
  → DateRange bei Diyanet laden (10x/Monat Limit!)
  → Als Jahres-Cache in Turso speichern
  → Alle weiteren Anfragen direkt aus Cache
```

### Diyanet Limits

| Endpunkt | Limit |
|----------|-------|
| DateRange (Jährlich) | 10x / Monat pro Stadt |
| Daily/Weekly/Monthly | 5x / Tag pro Stadt |

---

## Deployment (Render.com)

### Umgebungsvariablen

```
AwqatSalahSettings__UserName         → Diyanet Login E-Mail
AwqatSalahSettings__Password         → Diyanet Passwort

TursoSettings__DatabaseUrl           → https://xxx.turso.io
TursoSettings__AuthToken             → dein-turso-token

MyApiClientSettings__ApiKeys__0__Key      → app1-key
MyApiClientSettings__ApiKeys__0__Name     → app1
MyApiClientSettings__ApiKeys__0__IsAdmin  → false

MyApiClientSettings__ApiKeys__10__Key     → admin-key
MyApiClientSettings__ApiKeys__10__Name    → app1a
MyApiClientSettings__ApiKeys__10__IsAdmin → true
```

### Branch

Die Produktion läuft auf dem `smartcache` Branch.

---

## Lokale Entwicklung

```bash
git clone https://github.com/DEINNAME/awqatsalah-api
cd awqatsalah-api
# appsettings.json mit eigenen Credentials befüllen
dotnet run --project DiyanetNamazVakti.Api
```

---

## Swagger UI

Erreichbar unter: `https://deine-api.onrender.com/swagger`

Authentifizierung über den **Authorize** Button → `X-API-Key` eingeben.

---

## Lizenz

Basiert auf [DinIsleriYuksekKurulu/AwqatSalah](https://github.com/DinIsleriYuksekKurulu/AwqatSalah).
