# Lian Li Theme Gallery Stats

This Cloudflare Worker stores download counts and ratings for the desktop app gallery.
Theme packages and previews stay on GitHub; this service only stores live stats.

## One-time setup

1. Install dependencies:

   ```powershell
   npm install
   ```

2. Log in to Cloudflare:

   ```powershell
   npx wrangler login
   ```

3. Create the D1 database:

   ```powershell
   npx wrangler d1 create lianli-theme-gallery
   ```

4. Copy `wrangler.toml.example` to `wrangler.toml`, then paste the database id from the previous command.

5. Create the tables:

   ```powershell
   npx wrangler d1 execute lianli-theme-gallery --remote --file=./schema.sql
   ```

6. Enable R2 in the Cloudflare Dashboard, then create the upload bucket:

   ```powershell
   npx wrangler r2 bucket create lianli-theme-submissions
   ```

7. Deploy:

   ```powershell
   npx wrangler deploy
   ```

8. Paste the deployed `workers.dev` URL into:

   ```text
   templates/gallery-stats-url.txt
   ```

The uploader posts theme packages to `POST /submissions`. New uploads are stored with `pending` status in D1 and R2.
