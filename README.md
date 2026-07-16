# Google Business Profile Review Extractor

A lightweight, automated command-line tool designed to extract public Google Maps reviews for a specific month and format them into a clean, tracking-ready CSV file. 

This program operates entirely using public data streams (no Google OAuth/account logins required) via the SerpApi engine.

---

## 🚀 Key Features

* **Targeted Extraction:** Query and isolate public reviews for any specific calendar month (using `YYYY-MM` format).
* **Automated CSV Formatting:** Generates structured CSV spreadsheets detailing guest names, ratings, specific category scores (food, service, atmosphere), review descriptions, and official owner responses.
* **Smart Boundary Detection:** Automatically stops scanning once it detects a threshold of reviews older than your target window to save API usage.
* **Cross-Platform Timezone Support:** Automatically converts review timestamps to Pacific Standard/Daylight Time (America/Vancouver) to align precisely with local business hours.

---

## 🛠️ Setup & Configuration

This application keeps sensitive API credentials secure by reading them from a local environment configuration file.

1. Locate or create a file named `.env` in the same folder as the application executable.
2. Open the `.env` file in any plain text editor (e.g., TextEdit on macOS or Notepad on Windows) and configure your credentials:

```env
SERP_API_KEY=your_serp_api_key_here
GOOGLE_MAPS_DATA_ID=your_google_maps_data_id_here