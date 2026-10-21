# wp-discovery — what the live WordPress site exposes

**When:** before Import I.2. Re-run it if the live site gains plugins, post types or taxonomies, or if a
mapping's assumptions look wrong.
**Needs:** nothing. Every call is an unauthenticated GET against `https://6311arts.com`.

Run by Import I.1 on **2026-10-21**. 30 calls, sequential, `User-Agent:
6311arts-importer/0.1 (migration reconnaissance; +https://6311arts.com)`. No authenticated calls, no media
binaries, no writes to the live site. Raw responses were kept in a scratch folder and not committed.

> **This report supersedes the inferences in `src/web/projects/site/legacy/6311-headless.php`.** That plugin is
> not installed on the live site, and several of its assumptions are wrong — see "Where the legacy plugin
> disagrees with reality" below. Treat this file as the source of truth for I.3 onwards.

## Headline answers

| Question I.1 had to settle | Answer |
|---|---|
| Is the spike's `6311/v1` namespace installed? | **No.** `/wp-json/6311/v1/classes` → `404`. Confirmed, not inferred. |
| Is there an `exhibition` post type? | **No.** 23 post types registered, none is `exhibition`. Exhibitions have **no WordPress source**. |
| Is there an artists source? | **Yes, and it is new:** an `artist` **taxonomy** on products (59 terms, 819 product assignments), plus the older artist-named `product_cat` terms. Not a CPT. |
| Is `fooevents/v1/events` public? | **Yes** (`200`), and it lists **8** events — but it returns raw post rows with **no meta**, so no dates. |
| Does any public endpoint carry the FooEvents date meta? | **No.** `/wp/v2/product` exposes `meta`, but only Astra theme and Jetpack keys. The dates need `wc/v3`. |
| Is `wc/v3` reachable unauthenticated? | **No.** `401`. A read-only WooCommerce key is genuinely required. |
| Is MailPoet present? | **Yes**, `mailpoet/v1` is registered. `/mailpoet/v1/subscribers` is `GET` but admin-only. |
| Is there a sitemap? | **Yes, but not where WordPress core puts it.** `/wp-sitemap.xml` → `404`; `robots.txt` points to `/sitemap.xml` (Jetpack). |

## Record counts

| Endpoint | Count | Source |
|---|---|---|
| `wp/v2/media` | **4,055** | `X-WP-Total` |
| `wp/v2/pages` | **23** | `X-WP-Total` |
| `wp/v2/posts` | **0** | `X-WP-Total` — there is no blog |
| `wc/store/v1/products` | **830** | `X-WP-Total` |
| `fooevents/v1/events` | **8** | response length (the endpoint ignores `per_page`) |
| `artist` taxonomy | **59** terms, **819** assignments | `X-WP-Total`, sum of `count` |
| `medium` taxonomy | **447** terms | `X-WP-Total` |
| `product_cat` taxonomy | **126** terms, **1,230** assignments | `X-WP-Total`, sum of `count` |
| `wc/store/v1/products/categories` | **73** | non-empty `product_cat` terms only |
| `/sitemap-1.xml` | **764** URLs: 741 `/product/*` + 23 pages | `<loc>` count |

830 products but 741 in the sitemap: ~89 are excluded from it (drafts, private or hidden). I.3 must paginate the
API, not read the sitemap, to get them all.

## Namespaces registered (58)

The ones that matter: `wp/v2`, `wc/store/v1`, `wc/v3`, `wc/v1`, `wc/v2`, `fooevents/v1`, `mailpoet/v1`,
`cptui/v1`, `wc/pos/v1/catalog`, `fl-controls/v1` (Beaver Builder), `astra/v1` + `astra_addon/v1`,
`jetpack/v4` + `jetpack-boost/v1` + `videopress/v1`, `search-filter/v1` + `search-filter-pro/v1`,
`wc-facebook/v1`, `wc/gla` (Google Listings), `google-site-kit/v1`, `akismet/v1`, `metricool/v1`.

Two worth noting beyond the import:

- **`wc/pos/v1/catalog`** — a WooCommerce POS plugin is installed. Worth knowing before Phase 5 decides what
  "Square owns the record" means in practice at the counter.
- **`fl-controls/v1` and the `fl-builder-template` post type** — Beaver Builder. Page and product bodies will
  carry builder markup, which `import.md` already requires stripping and reporting.

## Post types (23 registered)

None is an exhibition. The content-bearing ones are `page`, `product` and `attachment`. The rest are plugin
or theme machinery: `feedback` (Jetpack **form responses** — personal data, `401`, never read, never imported),
`jetpack_form`, `fl-builder-template`, `mailpoet_email`, `wp_knowledge` ("Guidelines", `401`),
`jp_pay_order`/`jp_pay_product`, the Jetpack search templates, and core's block/template/font types.

`post` exists but holds **0** records.

## Taxonomies (10 registered)

| Taxonomy | On | Terms | Hierarchical | Note |
|---|---|---|---|---|
| **`artist`** | `product` | 59 | No | **The artist roster.** New since the spike. `rest_base: artist`. |
| **`medium`** | `product` | 447 | No | "Mediums". **Unusable as-is** — see below. |
| `product_cat` | `product` | 126 | Yes | Mixes artist names *and* product types. The spike's deny-list problem. |
| `product_brand`, `product_tag` | `product` | — | — | Not used for anything we import. |
| `category`, `post_tag` | `post` | — | — | Irrelevant: 0 posts. |
| `nav_menu`, `wp_pattern_category`, `wp_knowledge_type` | — | — | — | Machinery. |

### The `artist` taxonomy is the roster to import

59 terms covering 819 of 830 products. Every term's `description` is **empty**, so there is still no artist bio
anywhere on the site — the CMS `Artist.Bio` has no source and staff must write it.

Data quality to expect (every one of these is in the live data):

- `6311arts` (4 products) — the gallery itself, not a person.
- `audrey-avril` (20) **and** `audrey-avrilmedium` (1) — a term created by a mis-paste.
- `socorro-mucino` (38) **and** `sorocco-mucino` (0) — a misspelling.
- `jesy-grose` (5) **and** `jesy-grose-tigerbtch` (26) — one artist, two terms.
- `nicold-pittman` (0) — empty.

### `product_cat` holds a second, inconsistent copy of the roster

126 terms: artist names *and* `artists` (a parent term, 141), `jewelry` (89), `prints` (203), `ceramics` (20),
`books-magazines` (4), `t-shirts` (18), `lamps` (11), `branded-merchandise` (2), `memberships` (3),
`classes` (4), `classes-workshops-events` (6), `workshops` (1, child of 1486), `events` (1), `class-supplies` (0),
`uncategorized` (0).

The `artists` parent is applied **inconsistently** — only a handful of artist terms (`susan-volk`,
`stephen-bell`, `ralph-skunkie-davis`) have `parent: 1445`; most have `parent: 0`. So a hierarchy check will not
separate artists from product types, which is why the spike needed a deny-list.

Spellings disagree with the `artist` taxonomy: `michael-winchester` / `mike-winchester`,
`hedda-lupin` / `hedda-lubin`, `edwinn-meyers` / `eddwin-meyers`, `anneterpstra` / `anne-terpstra`.

**56 of the 110 artist-named `product_cat` terms have `count: 0`** — half that roster has never had a piece
listed. The `artist` taxonomy is far tighter: 2 of its 59 terms are empty, and **0 of 59 carry a description**.

### The `medium` taxonomy cannot be imported as a medium

447 terms, because staff have been pasting whole descriptions into the field. Real term slugs:

- `4-inch-bowl-showcases-the-captivating-beauty-of-wood-in-motion-flowing-worm-lines-filled-with-black-resin-trace-organic-paths-across-the-surface`
- `aaa-grade-freshwater-baroque-pearl-18k-gold-filled-paperclip-chain-hand-wire-wrapped-one-of-a-kind-pearl-natural-variations-in-shape`
- `and-designed-for-everyday-use`, `and-timeless`, `blend-seamlessly`
- `b`, `aluminum-dfimensions`, `automative-paints`, `brass-w`

Alongside perfectly good ones: `ceramic` (26), `ceramics` (24), `copper` (22), `acrylic` (5), `bronze` (3).
Mapping this into `Artist.Medium` or anywhere else needs a human-curated allow-list, not a transform.

## Products — what the public API gives, and what it withholds

`wc/store/v1/products` returns, per product: `id`, `slug`, `name`, `permalink`, **`sku`**, `prices`
(`price`/`regular_price`/`sale_price` as **strings in minor units** with `currency_minor_unit: 2`),
`categories` (`product_cat` only), `tags`, `brands`, `attributes`, `images[]` (`id`, `src`, `thumbnail`,
`srcset`, **`alt`**), `is_in_stock`, `low_stock_remaining`, `stock_availability.text`, `description`,
`short_description`, `type`, `variations`.

It **does not** return the `artist` or `medium` taxonomies, and it does not return an exact stock quantity.

`/wp/v2/product` **is public** and fills the first gap: it returns `artist`, `medium`, `product_cat` and
`product_tag` term ids plus `featured_media`, `title`, `content`, `excerpt`, `date`, `modified`, `link`. Its
`meta` is **only** Astra and Jetpack keys — 35 of them, none WooCommerce, none FooEvents.

So the public pair `wc/store/v1/products` + `wp/v2/product` covers everything except **exact stock** and
**FooEvents dates**.

Observed stock signal: `stock_availability.text` is human prose — `"1 in stock"`, `"8 in stock"`, `"7 in stock"`,
or `""`. `low_stock_remaining` is `1` for single pieces and `null` otherwise. Parsing prose for a starting
quantity is not acceptable; `wc/v3` gives the integer.

Samples confirming the shapes:

| Product | id | SKU | price | stock | images | artist tax |
|---|---|---|---|---|---|---|
| Moonlight (jewelry) | 5665 | `5114915` | `14375` | `1 in stock`, low=1 | 1, alt present | `[2029]` |
| 3 Shapes Long Necklace | 4700 | `182139T` | `14000` | `1 in stock`, low=1 | 1, alt present | `[2019]` |
| Sketchbook | 8026 | `X818145` | `1000` | in stock | **0 images** | `[2718]` |
| 6311 Tote Bags (merch) | 2729 | `629358P` | — | `7 in stock` | — | — |
| gray membership | 3338 | *(empty)* | `8000` | `""` | 1, **no alt** | — |
| Relief Printmaking (class) | 7857 | *(empty)* | `30000` | `8 in stock` | 1 flyer, **no alt** | `[]` |

Artwork has SKUs; **classes and memberships have none** — the CMS will suggest `CLS-…` and `ART-…` for those.

## Classes — the attributes are not what the legacy plugin reads

The 8 FooEvents products carry these **custom** (non-taxonomy) attributes:

| Attribute name | Example value |
|---|---|
| `Instructor` | `Nicholas Waguespack` |
| `Level` | `Beginner` |
| `Day of the Week` | `Sunday` |
| `Time of Day` | `Afternoon` |
| `Class Type` | `Printmaking` |

**There is no `sessions`, `cadence` or `capacity` attribute.** The legacy plugin reads lowercase
`instructor`, `type`, `level`, `sessions`, `cadence`, `capacity` — three of those six do not exist, and the
other three are spelled differently.

Where the missing values actually live:

- **Session count** — only in the **title**: `"…, 5-Week Course"`. Nowhere structured.
- **Cadence** — `Day of the Week` (`Sunday`), coarser than the spike's `"Saturdays"` prose.
- **Capacity** — nowhere. `stock_availability.text` (`"8 in stock"`) is seats *remaining*, which is Square's
  field in the new model, not the CMS's capacity.

The 8 events, with their slugs:

| id | slug | title |
|---|---|---|
| 7857 | `intro-to-screen-printing-sundays-11-8-10am-1pm-5-week-course` | Relief Printmaking, Sundays, 11/8, 10AM-1PM, 5-Week Course |
| 7855 | `intermediate-screen-printing-with-diego-from-rathaus-wednesdays-11-4-630-930pm-5-week-course` | Intermediate Screen Printing with Diego from Rathaus, Wednesdays, 11/4, 6:30-9:30PM, 5-Week Course |
| 7735 | `printmaking-playground-sundays-11-8-200-500pm` | Printmaking Playground, Sundays, 11/8, 2:00-5:00PM 5-Week Course |
| 7662 | `intro-to-screen-printing-saturday-10-17-200-500pm` | Intro to Screen Printing-Saturdays, 10/24 2:00-5:00PM 5-week Course |
| 7660 | `day-of-the-dead-ofrenda-alter-making-workshop-with-candice-thursday-11-22-600-900pm` | Day of the Dead-Ofrenda/Alter making Workshop-Thursday, 10/22 6:00-9:00pm |
| 7653 | `intro-to-scratchboard-friday-11-6-630-930pm` | Intro to Scratchboard - Friday, 11/6 6:30-9:30pm |
| 4327 | `boats-and-pianos-opening-reception` | "Boats & Pianos" Opening Reception |
| 4275 | `bonsai-101-living-sculpture` | Bonsai 101 - Living Sculpture Sunday 10/25 2-4pm |

**Slugs and titles disagree on four of the eight.** 7857's slug says "intro to screen printing", its title says
"Relief Printmaking". 7662's slug says `10-17`, its title says `10/24`. 7660's slug says `11-22`, its title says
`10/22`. These are renamed or duplicated products whose permalinks were never updated. Consequences: the title is
the content, the slug is only an address, and **neither is a reliable source for a date** — the FooEvents meta is.

## Memberships

3 products in `memberships`: `gray-membership` (`8000`), `black-membership` (`12000`),
`blue-membership` (`16000`). No SKU, no attributes, one image each with **no alt text**.

Perks are **prose in the description**, not a list:

> "Support 6311arts community art center with a Gray membership for just $80 a year you can get 5% off all
> workshops and classes, an invite to the annual membership party, and a 6311arts tote."

So the discount (`5%`) and the cadence (annual) are stated but unstructured, and `MembershipPlan.Perks` is an
ordered list of rows. There is no `<li>` markup to parse. This mapping needs staff input, not a transform.

There is also a `/gray-membership/` **page** (id 3345) separate from the product.

## Pages (23)

Content pages: `about`, `artists`, `events`, `gallery`, `home` (served at `/`), `memberships`, `shop`,
`privacy-policy`, `submit-your-work`, `gray-membership`, `classes-test-page`.

Plugin and account pages: `cart`, `checkout`, `my-account`, `login`, `register`, `forgot_password`,
`change_password`, `guest`, `member_panel`, `thank_you`, `cancel_payment`, `refund_returns`.

Two findings for the Pages mapping:

- **`privacy-policy` exists** (id 3137) with a substantial body (~36 KB of JSON).
- **`terms-and-conditions` does not exist.** `?slug=terms-and-conditions` → `X-WP-Total: 0`. The spike's route
  `/terms` has no source; staff must write it.

## Media

4,055 attachments. `/wp/v2/media` gives `source_url` as the **bare origin URL**
(`https://6311arts.com/wp-content/uploads/2026/10/Nexus-Jewelry-17-3.jpeg`), not the Photon CDN form — so I.4
can download originals directly. `alt_text` **is** populated on the items sampled (`"Nexus Jewelry 17"`), which
is better than the spike data suggested, but the real missing-alt figure comes from the snapshot at I.4 (this
pass deliberately did not page 4,055 records).

`media_details.sizes` holds **11** generated sizes per image, all skipped per `import.md`. Originals are large
(2528×2560, 2560×1920 in the sample).

Product image URLs inside Store API responses **are** Photon-wrapped
(`https://i0.wp.com/6311arts.com/wp-content/uploads/…?fit=2400%2C3300&ssl=1`); the media library's `source_url`
is not. Prefer the media library.

## Old-URL inventory for the redirect plan

`robots.txt` names `/sitemap.xml` and `/news-sitemap.xml`. `/sitemap.xml` is a Jetpack index →
`/sitemap-1.xml` (764 URLs: **741 `/product/*`** + the 23 pages) and `/image-sitemap-index-1.xml` → a further
index of 4 image sitemaps.

Shapes to redirect, confirmed live:

- `/product/<slug>` — **741 of them**, one namespace shared by artwork, classes, memberships and merch. Only the
  mapping that claims each product can say where it goes.
- `/product-category/<slug>` — 126 terms' archives (`link` is present on every term).
- `/about/`, `/shop/`, `/events/`, `/gallery/`, `/artists/`, `/memberships/`, `/privacy-policy/`,
  `/submit-your-work/`, `/gray-membership/`, `/classes-test-page/`
- `/cart/`, `/checkout/`, `/my-account/`, `/login/`, `/register/`, `/forgot_password/`, `/change_password/`,
  `/guest/`, `/member_panel/`, `/thank_you/`, `/cancel_payment/`, `/refund_returns/`

`robots.txt` also disallows `/*?add-to-cart=`, `/wp-content/uploads/wc-logs/`,
`/wp-content/uploads/woocommerce_transient_files/` and `/wp-content/uploads/woocommerce_uploads/`.

The `/artist/<slug>` shape the spike redirects is **not** in the sitemap — the `artist` taxonomy's `rest_base`
is `artist`, but whether its term archives are public at `/artist/<slug>` was not probed. I.7's redirects
mapping should check one before emitting 59 redirects for it.

## Where the legacy plugin disagrees with reality

| `6311-headless.php` assumes | Live site |
|---|---|
| `6311/v1/classes|exhibitions|artists` serve the site | `404` — never installed |
| An `exhibition` post type exists (it registers one) | No such type. No exhibition content at all. |
| Artists are `product_cat` terms minus a 13-slug deny-list | A dedicated **`artist` taxonomy** (59 terms, 819 assignments) exists and is the better source |
| Artist `medium` comes from the term description | Every `artist` term description is **empty**; a separate `medium` taxonomy exists but is 447 junk terms |
| Class attributes are `instructor`, `type`, `level`, `sessions`, `cadence`, `capacity` | `Instructor`, `Level`, `Day of the Week`, `Time of Day`, `Class Type`. **No sessions, cadence or capacity.** |
| `capacity` is an attribute, `spotsLeft` is stock | No capacity anywhere; stock is prose in `stock_availability.text` |
| Classes are products in `classes-workshops-events` | That category has 6; `classes` has 4, `workshops` 1, `events` 1 — and `fooevents/v1/events` lists **8** |
| Only upcoming classes matter (it drops anything >24h past) | Past classes still own `/product/<slug>` URLs that need a destination |

## Verify

A re-run should still show: `6311/v1` → 404, `wc/v3` → 401, `fooevents/v1/events` → 200 with no meta,
no `exhibition` post type, and an `artist` taxonomy with ~59 terms. If any of those flip, the mappings in I.7
need revisiting before they run.

## Roll back

Nothing to roll back. Every call was a GET and nothing was written to the live site.

---

## Recommendation

### 1. A read-only WooCommerce key is required. Get one.

It is the **only** way to reach two things the import cannot do without:

- **FooEvents date and time meta** (`wc/v3/products?...meta_data`) — the start and end of every class session.
  Confirmed absent from `wp/v2/product`'s `meta`, from `wc/store/v1`, and from `fooevents/v1/events`.
- **Exact stock quantity** — `StartingStockQuantity` and the one-of-a-kind flag. The public API gives only
  `"1 in stock"` prose and a `low_stock_remaining` hint.

Scope it to **Read** only, as I.2 specifies, and revoke it at I.14.

### 2. An Application Password is **not** needed.

Everything else the import reads is public: pages, media (with `alt_text`), products, the `artist`, `medium` and
`product_cat` taxonomies, and per-product taxonomy assignment via `/wp/v2/product`. The only admin-gated things
are `feedback` (Jetpack form responses — personal data we must never import), `wp_knowledge`, and MailPoet
subscribers, which is a CSV export with consent timestamps, not a REST read (I.10).

So I.2 should create **one** credential, not two. If a later prompt finds a gap, add the Application Password
then.

### 3. A WXR export is not worth requesting. An uploads copy might be.

- **WXR: no.** The REST API covers every content type we import, with `modified` timestamps for I.13's delta.
  WXR would add nothing but a second parser.
- **Uploads copy: maybe.** 4,055 originals at 2.5K×2.5K is plausibly 5–15 GB over 4,055 HTTPS requests at two
  concurrent. If the host can hand over a tarball of `wp-content/uploads/` (excluding `wc-logs/`,
  `woocommerce_uploads/` and `woocommerce_transient_files/`, which `robots.txt` already walls off), I.4 becomes a
  local checksum pass instead of a long download. Ask for it; if it's awkward, I.4's resumable downloader handles
  it either way.

### 4. Three mappings need your decisions before I.7 can run

- **Artists: which source?** The `artist` taxonomy (59 terms, 819 assignments, no bios) is cleaner than the 126
  `product_cat` terms, but the two disagree on spelling and the `product_cat` copy is what `/product-category/`
  URLs redirect from. Recommendation: import the roster from the **`artist` taxonomy**, and use `product_cat`
  only to build redirects. Both need a merge list for the duplicate terms named above.
- **Classes: sessions have no dates.** Even with the WooCommerce key, FooEvents gives one date per product. A
  "5-Week Course" has weeks 2–5 nowhere in the data. Recommendation: import one `ClassSession` and mark the run
  `NeedsReview`, per `add-import-mapping`'s "never a guess". The alternative — synthesising weekly dates from
  `Day of the Week` — invents facts that then get pushed to Square as sellable sittings.
- **Merch: which categories?** `branded-merchandise` (2) is obvious, and `t-shirts` (18) is clearly merch too.
  `lamps` (11), `class-supplies` (0) and `books-magazines` (4) are judgement calls — `books-magazines` is in
  `ShopPolicy.Categories` so it is artwork, but `lamps` is in no allow-list and would be dropped silently. Name
  the merch set explicitly in I.7's mapping table.
