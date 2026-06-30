---
name: grab
description: Add a movie or TV show to the home media server for download. Use when the user says "grab X", "download X", "add X to Jellyfin/Sonarr/Radarr", or "I want to watch X". Figures out movie vs TV automatically, searches the correct service, confirms the match, and adds it. Requires the /media-server skill context.
---

# /grab — Add Content to the Media Server

Adds a movie or TV show to the home server at `reset@192.168.1.28`.

## Input

`/grab <title>` — a movie or show title. Year optional but helpful for disambiguation (e.g. `/grab Vacation 1983`).

## Behavior

1. **Determine type** — if the user specifies "movie" or "show", use that. Otherwise search both Sonarr and Radarr and pick whichever returns a confident first match; if both match, ask the user.

2. **Look up** — always read API keys live from config.xml:
   ```bash
   SONARR_KEY=$(ssh reset@192.168.1.28 "grep -oP '(?<=<ApiKey>)[^<]+' /opt/arr/sonarr/config.xml")
   RADARR_KEY=$(ssh reset@192.168.1.28 "grep -oP '(?<=<ApiKey>)[^<]+' /opt/arr/radarr/config.xml")
   ```

3. **Check if already in library** before adding. If already present, say so and skip.

4. **Confirm match** — show the user: title, year, and type (movie/series). If the top result doesn't look right, show the next 2 options.

5. **Add it**:
   - Movies → Radarr, quality profile **HD-1080p (id=4)**. Never use "Any" — it grabs BR-DISK full disc images (20–70 GB).
   - TV → Sonarr, quality profile **Any (id=1)**, monitor="all", searchForMissingEpisodes=true.
   - Anime TV → same as TV but set seriesType="anime" after adding (PUT /api/v3/series/{id}).

6. **Confirm** — report back: "Added [title] to [Radarr/Sonarr]. It'll appear in Jellyfin once downloaded (~5–30 min)."

## Movie add payload

```bash
curl -sf -X POST http://192.168.1.28:7878/api/v3/movie \
  -H "X-Api-Key: $RADARR_KEY" -H 'Content-Type: application/json' \
  -d "{\"tmdbId\":TMDB_ID,\"title\":\"TITLE\",\"year\":YEAR,\"qualityProfileId\":4,\"rootFolderPath\":\"/downloads/movies\",\"monitored\":true,\"addOptions\":{\"searchForMovie\":true}}"
```

## TV show add payload

```bash
curl -sf -X POST http://192.168.1.28:8989/api/v3/series \
  -H "X-Api-Key: $SONARR_KEY" -H 'Content-Type: application/json' \
  -d "{\"title\":\"TITLE\",\"tvdbId\":TVDB_ID,\"qualityProfileId\":1,\"rootFolderPath\":\"/downloads/tv-shows\",\"monitored\":true,\"addOptions\":{\"searchForMissingEpisodes\":true,\"monitor\":\"all\"}}"
```

## Already in library check

```bash
# Movies
curl -sf http://192.168.1.28:7878/api/v3/movie -H "X-Api-Key: $RADARR_KEY" | \
  python3 -c "import sys,json; [print(m['title'],m['year']) for m in json.load(sys.stdin) if 'TITLE' in m['title'].lower()]"

# TV
curl -sf http://192.168.1.28:8989/api/v3/series -H "X-Api-Key: $SONARR_KEY" | \
  python3 -c "import sys,json; [print(s['title'],s.get('year','')) for s in json.load(sys.stdin) if 'TITLE' in s['title'].lower()]"
```

## Edge cases

- **Sequel/disambiguation**: If the search returns multiple plausible matches (same title, different years), show a numbered list and ask which one.
- **Anime**: If it looks like anime (check title against TVDB result's `seriesType` field, or if the user says "anime"), set `seriesType: "anime"` via PUT after adding, otherwise absolute episode numbering won't match.
- **Collection folders**: A title like "Indiana Jones" may already have files on disk via a collection folder. Check the Radarr library before adding — if the film is already there (even unmonitored), just enable monitoring via PUT rather than re-adding.
