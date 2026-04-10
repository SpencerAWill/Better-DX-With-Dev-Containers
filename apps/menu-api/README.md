# Menu API

ASP.NET Core REST API for menu data.

## Purpose

Serves menu data (categories, items, modifiers, pricing) to consuming services. Read-heavy with a caching layer for performance.

- **Readers:** ordering-api, kds-api
- **Writers:** admin-api
