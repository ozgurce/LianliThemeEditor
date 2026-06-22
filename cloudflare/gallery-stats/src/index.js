const corsHeaders = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET,POST,OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type, Authorization"
};

export default {
  async fetch(request, env) {
    if (request.method === "OPTIONS") {
      return new Response(null, { headers: corsHeaders });
    }

    try {
      const url = new URL(request.url);
      const parts = url.pathname.split("/").filter(Boolean);

      if (request.method === "GET" && url.pathname === "/themes/stats") {
        const voterKey = normalizeKey(url.searchParams.get("voterKey"));
        return json(await listStats(env.DB, voterKey));
      }

      if (request.method === "GET" && url.pathname === "/themes/community") {
        return json(await listCommunityThemes(request, env.DB));
      }

      if (request.method === "POST" && url.pathname === "/submissions") {
        return json(await createSubmission(request, env));
      }

      if (parts[0] === "admin") {
        requireAdmin(request, env);
        if (request.method === "GET" && parts.length === 2 && parts[1] === "submissions") {
          return json(await listSubmissions(env.DB, url.searchParams.get("status") || "pending"));
        }

        if (request.method === "POST" && parts.length === 4 && parts[1] === "submissions") {
          const id = normalizeKey(parts[2]);
          if (parts[3] === "approve") {
            return json(await reviewSubmission(env, id, "approved"));
          }

          if (parts[3] === "reject") {
            const body = await readJson(request);
            return json(await reviewSubmission(env, id, "rejected", normalizeText(body.note, 500)));
          }

          if (parts[3] === "delete") {
            return json(await deleteSubmission(env, id));
          }
        }
      }

      if (request.method === "GET" && parts.length === 3 && parts[0] === "submissions" && parts[1] === "file") {
        return serveSubmissionFile(request, env, parts[2]);
      }

      if (request.method === "POST" && parts.length === 3 && parts[0] === "themes") {
        const themeId = normalizeThemeId(parts[1]);
        if (!themeId) {
          return json({ error: "Invalid theme id" }, 400);
        }

        if (parts[2] === "download") {
          return json(await recordDownload(env.DB, themeId));
        }

        if (parts[2] === "vote") {
          const body = await readJson(request);
          const voterKey = normalizeKey(body.voterKey);
          const rating = Number.parseInt(body.rating, 10);
          if (!voterKey) {
            return json({ error: "Missing voterKey" }, 400);
          }

          if (!Number.isInteger(rating) || rating < 1 || rating > 5) {
            return json({ error: "Rating must be between 1 and 5" }, 400);
          }

          return json(await recordVote(env.DB, themeId, voterKey, rating));
        }
      }

      return json({ error: "Not found" }, 404);
    } catch (error) {
      if (error instanceof Response) {
        return error;
      }

      return json({ error: error?.message || "Server error" }, 500);
    }
  }
};

async function listStats(db, voterKey) {
  const stats = await db
    .prepare(
      `SELECT theme_id AS id,
              downloads,
              vote_count AS voteCount,
              CASE WHEN vote_count > 0 THEN ROUND(CAST(vote_total AS REAL) / vote_count, 2) ELSE 0 END AS averageRating
         FROM theme_stats
        ORDER BY theme_id`
    )
    .all();

  const themes = stats.results || [];
  if (voterKey && themes.length > 0) {
    const votes = await db
      .prepare("SELECT theme_id AS id, rating AS userRating FROM theme_votes WHERE voter_key = ?1")
      .bind(voterKey)
      .all();
    const userRatings = new Map((votes.results || []).map((vote) => [vote.id, vote.userRating]));
    for (const theme of themes) {
      theme.userRating = userRatings.get(theme.id) || 0;
    }
  }

  return { themes };
}

async function listCommunityThemes(request, db) {
  const origin = new URL(request.url).origin;
  const rows = await db
    .prepare(
      `SELECT id,
              theme_name AS name,
              author,
              device_model AS deviceModel,
              package_key AS packageKey,
              preview_key AS previewKey
         FROM theme_submissions
        WHERE status = 'approved'
        ORDER BY reviewed_at DESC, created_at DESC
        LIMIT 200`
    )
    .all();

  const themes = (rows.results || []).map((row) => ({
    id: row.id,
    name: row.name,
    author: row.author,
    deviceModel: row.deviceModel,
    packageUrl: `${origin}/submissions/file/${encodeURIComponent(row.packageKey)}`,
    previewUrl: row.previewKey ? `${origin}/submissions/file/${encodeURIComponent(row.previewKey)}` : ""
  }));
  return { themes };
}

async function recordDownload(db, themeId) {
  await db
    .prepare(
      `INSERT INTO theme_stats (theme_id, downloads, vote_count, vote_total)
       VALUES (?1, 1, 0, 0)
       ON CONFLICT(theme_id) DO UPDATE SET downloads = downloads + 1`
    )
    .bind(themeId)
    .run();

  return getStats(db, themeId);
}

async function recordVote(db, themeId, voterKey, rating) {
  await db
    .prepare("INSERT OR IGNORE INTO theme_stats (theme_id, downloads, vote_count, vote_total) VALUES (?1, 0, 0, 0)")
    .bind(themeId)
    .run();

  const existing = await db
    .prepare("SELECT rating FROM theme_votes WHERE theme_id = ?1 AND voter_key = ?2")
    .bind(themeId, voterKey)
    .first();

  if (existing) {
    await db.batch([
      db
        .prepare("UPDATE theme_votes SET rating = ?3, updated_at = CURRENT_TIMESTAMP WHERE theme_id = ?1 AND voter_key = ?2")
        .bind(themeId, voterKey, rating),
      db
        .prepare("UPDATE theme_stats SET vote_total = vote_total - ?2 + ?3 WHERE theme_id = ?1")
        .bind(themeId, existing.rating, rating)
    ]);
  } else {
    await db.batch([
      db
        .prepare("INSERT INTO theme_votes (theme_id, voter_key, rating) VALUES (?1, ?2, ?3)")
        .bind(themeId, voterKey, rating),
      db
        .prepare("UPDATE theme_stats SET vote_count = vote_count + 1, vote_total = vote_total + ?2 WHERE theme_id = ?1")
        .bind(themeId, rating)
    ]);
  }

  const stats = await getStats(db, themeId);
  stats.userRating = rating;
  return stats;
}

async function getStats(db, themeId) {
  const row = await db
    .prepare(
      `SELECT theme_id AS id,
              downloads,
              vote_count AS voteCount,
              CASE WHEN vote_count > 0 THEN ROUND(CAST(vote_total AS REAL) / vote_count, 2) ELSE 0 END AS averageRating
         FROM theme_stats
        WHERE theme_id = ?1`
    )
    .bind(themeId)
    .first();

  return row || { id: themeId, downloads: 0, voteCount: 0, averageRating: 0 };
}

async function createSubmission(request, env) {
  if (!env.SUBMISSIONS) {
    throw new Error("R2 bucket binding SUBMISSIONS is not configured.");
  }

  const form = await request.formData();
  const packageFile = form.get("package");
  const previewFile = form.get("preview");
  const themeName = normalizeText(form.get("themeName"), 120);
  const author = normalizeText(form.get("author"), 80);
  const contact = normalizeText(form.get("contact"), 160);
  const description = normalizeText(form.get("description"), 1000);
  const deviceModel = normalizeText(form.get("deviceModel"), 80);

  if (!themeName) {
    return { ok: false, error: "Theme name is required." };
  }

  if (!author) {
    return { ok: false, error: "Author name is required." };
  }

  if (!isSupportedDevice(deviceModel)) {
    return { ok: false, error: "Unsupported device model." };
  }

  if (!packageFile || typeof packageFile === "string" || packageFile.size <= 0) {
    return { ok: false, error: "Theme package is required." };
  }

  if (packageFile.size > 100 * 1024 * 1024) {
    return { ok: false, error: "Theme package is too large. Maximum size is 100 MB." };
  }

  const packageName = safeFileName(packageFile.name || "theme.lltheme");
  if (!packageName.toLowerCase().endsWith(".lltheme") && !packageName.toLowerCase().endsWith(".zip")) {
    return { ok: false, error: "Only .lltheme and .zip files are accepted." };
  }

  const id = crypto.randomUUID();
  const packageKey = `pending/${id}/package/${packageName}`;
  await env.SUBMISSIONS.put(packageKey, packageFile.stream(), {
    httpMetadata: {
      contentType: packageFile.type || "application/octet-stream"
    }
  });

  let previewKey = "";
  let previewName = "";
  if (previewFile && typeof previewFile !== "string" && previewFile.size > 0) {
    if (previewFile.size > 10 * 1024 * 1024) {
      return { ok: false, error: "Preview image is too large. Maximum size is 10 MB." };
    }

    previewName = safeFileName(previewFile.name || "preview.png");
    previewKey = `pending/${id}/preview/${previewName}`;
    await env.SUBMISSIONS.put(previewKey, previewFile.stream(), {
      httpMetadata: {
        contentType: previewFile.type || "application/octet-stream"
      }
    });
  }

  await env.DB
    .prepare(
      `INSERT INTO theme_submissions
       (id, theme_name, author, contact, device_model, description, package_key, package_file_name, package_size, preview_key, preview_file_name, status)
       VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, 'pending')`
    )
    .bind(id, themeName, author, contact, deviceModel, description, packageKey, packageName, packageFile.size, previewKey, previewName)
    .run();

  return { ok: true, id, status: "pending" };
}

async function listSubmissions(db, status) {
  status = ["pending", "approved", "rejected"].includes(status) ? status : "pending";
  const rows = await db
    .prepare(
      `SELECT id,
              theme_name AS themeName,
              author,
              contact,
              device_model AS deviceModel,
              description,
              package_key AS packageKey,
              package_file_name AS packageFileName,
              package_size AS packageSize,
              preview_key AS previewKey,
              preview_file_name AS previewFileName,
              status,
              created_at AS createdAt,
              reviewed_at AS reviewedAt,
              review_note AS reviewNote
         FROM theme_submissions
        WHERE status = ?1
        ORDER BY created_at DESC
        LIMIT 100`
    )
    .bind(status)
    .all();
  return { submissions: rows.results || [] };
}

async function reviewSubmission(env, id, status, note = "") {
  if (!id) {
    return { ok: false, error: "Missing submission id" };
  }

  const existing = await env.DB
    .prepare("SELECT * FROM theme_submissions WHERE id = ?1")
    .bind(id)
    .first();
  if (!existing) {
    return { ok: false, error: "Submission was not found" };
  }

  if (existing.status !== "pending") {
    return { ok: false, error: `Submission is already ${existing.status}` };
  }

  let packageKey = existing.package_key;
  let previewKey = existing.preview_key || "";
  if (status === "approved") {
    packageKey = await moveR2Object(env.SUBMISSIONS, existing.package_key, `approved/${id}/package/${existing.package_file_name}`);
    if (previewKey) {
      previewKey = await moveR2Object(env.SUBMISSIONS, previewKey, `approved/${id}/preview/${existing.preview_file_name || "preview"}`);
    }
  }

  await env.DB
    .prepare(
      `UPDATE theme_submissions
          SET status = ?2,
              package_key = ?3,
              preview_key = ?4,
              reviewed_at = CURRENT_TIMESTAMP,
              review_note = ?5
        WHERE id = ?1`
    )
    .bind(id, status, packageKey, previewKey, note)
    .run();

  return { ok: true, id, status };
}

async function deleteSubmission(env, id) {
  if (!id) {
    return { ok: false, error: "Missing submission id" };
  }

  const existing = await env.DB
    .prepare("SELECT * FROM theme_submissions WHERE id = ?1")
    .bind(id)
    .first();
  if (!existing) {
    return { ok: false, error: "Submission was not found" };
  }

  if (existing.package_key) {
    await env.SUBMISSIONS.delete(existing.package_key);
  }

  if (existing.preview_key) {
    await env.SUBMISSIONS.delete(existing.preview_key);
  }

  await env.DB
    .prepare("DELETE FROM theme_submissions WHERE id = ?1")
    .bind(id)
    .run();

  return { ok: true, id, status: "deleted" };
}

async function moveR2Object(bucket, sourceKey, destinationKey) {
  const object = await bucket.get(sourceKey);
  if (!object) {
    throw new Error(`R2 object was not found: ${sourceKey}`);
  }

  await bucket.put(destinationKey, object.body, {
    httpMetadata: object.httpMetadata,
    customMetadata: object.customMetadata
  });
  await bucket.delete(sourceKey);
  return destinationKey;
}

async function serveSubmissionFile(request, env, encodedKey) {
  if (!env.SUBMISSIONS) {
    return json({ error: "R2 bucket binding SUBMISSIONS is not configured." }, 500);
  }

  const key = decodeURIComponent(encodedKey || "");
  if (!key.startsWith("approved/") && !key.startsWith("pending/")) {
    return json({ error: "Invalid file key" }, 400);
  }

  if (key.startsWith("pending/")) {
    requireAdmin(request, env);
  }

  const object = await env.SUBMISSIONS.get(key);
  if (!object) {
    return json({ error: "File not found" }, 404);
  }

  return new Response(object.body, {
    headers: {
      ...corsHeaders,
      "Content-Type": object.httpMetadata?.contentType || "application/octet-stream",
      "Cache-Control": key.startsWith("approved/") ? "public, max-age=3600" : "no-store"
    }
  });
}

async function readJson(request) {
  try {
    return await request.json();
  } catch {
    return {};
  }
}

function normalizeThemeId(value) {
  return decodeURIComponent(value || "").trim().slice(0, 160);
}

function normalizeKey(value) {
  return String(value || "").trim().slice(0, 160);
}

function normalizeText(value, maxLength) {
  return String(value || "").replace(/\s+/g, " ").trim().slice(0, maxLength);
}

function isSupportedDevice(value) {
  return [
    "hydroshift-ii-lcd-s",
    "hydroshift-ii-lcd-c",
    "universal-screen-8.8-inch",
    "vm-9.2-inch"
  ].includes(value);
}

function safeFileName(value) {
  const name = String(value || "file").split(/[\\/]/).pop() || "file";
  return name.replace(/[^a-zA-Z0-9._-]/g, "-").replace(/-+/g, "-").slice(0, 120);
}

function requireAdmin(request, env) {
  if (!env.ADMIN_TOKEN) {
    throw new Error("ADMIN_TOKEN is not configured.");
  }

  const expected = `Bearer ${String(env.ADMIN_TOKEN).trim()}`;
  const actual = request.headers.get("Authorization") || "";
  if (actual !== expected) {
    throw new Response(JSON.stringify({ error: "Unauthorized" }), {
      status: 401,
      headers: {
        ...corsHeaders,
        "Content-Type": "application/json; charset=utf-8"
      }
    });
  }
}

function json(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      ...corsHeaders,
      "Content-Type": "application/json; charset=utf-8"
    }
  });
}
