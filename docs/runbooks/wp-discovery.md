# wp-discovery — what the live WordPress site exposes

**When:** before Import I.2. Re-run it if the live site gains plugins, post types or taxonomies, or if a
mapping's assumptions look wrong.
**Needs:** nothing. Every call is an unauthenticated GET against `https://6311arts.com`.

First run by Import I.1 on 2026-10-21. **Re-verified and extended on 2026-10-22**: 32 calls, sequential (one in
flight, 1 s apart), `User-Agent: 6311arts-importer/0.1 (migration reconnaissance; +https://6311arts.com)`, no
`429`s. No authenticated calls, no media binaries, nothing under `feedback`, `jp_pay_order`, `wp_knowledge` or
MailPoet subscribers, no writes to the live site. Raw responses were kept in a scratch folder and not committed.

> **This report supersedes the inferences in `src/web/projects/site/legacy/6311-headless.php`.** That plugin is
> not installed on the live site, and several of its assumptions are wrong — see "Where the legacy plugin
> disagrees with reality" below. Treat this file as the source of truth for I.3 onwards.

## What the 2026-10-22 re-run changed

The five facts "Verify" pins all held. What moved:

| | 10-21 | 10-22 | Why it matters |
|---|---|---|---|
| **A WooCommerce↔Square sync plugin (`sws/v1`)** | missed | **found, 169 routes** | 6311 already syncs WooCommerce with Square. I.9's match pass is matching *against items this plugin created*. See "Square is already connected". |
| **Variable products** | not counted | **99** | Variation price, stock and SKU are only in `wc/v3`. `ShopItem` has one price. A mapping decision. |
| Products, `wp/v2/product` | not counted | **873** | The full published list. The Store API hides 76 of them. I.3 enumerates from here. |
| Products, `wc/store/v1` | 830 | **797** | Store API count moves daily; not a source of truth. |
| Sitemap `/product/*` URLs | 741 | **706** | |
| Media | 4,055 | **4,138** | +83 uploads in a day: the site is actively edited. Plan I.13's deltas accordingly. |
| `artist` taxonomy | 59 terms, 819 assignments, 2 empty | **58 terms, 739 assignments, 7 empty** | |
| `medium` taxonomy | 447 | **451** | Still growing with junk. |
| FooEvents events | 8 | **9** | New: 8177 "Express Screen Printing Workshop". **7662's title changed date overnight** (10/24 → 11/7). |
| **`steve-oconnell`** | — | **33 products in `product_cat`, absent from `artist`** | The `artist` taxonomy alone loses an artist. Recommendation 4 is revised. |
| **`/artist/<slug>/` archives** | not probed | **public, `200`**, self-canonical | The 58 artist archive URLs need redirects. |
| **Jetpack/WordPress.com newsletter** | not noticed | `wpcom/v2/subscribers/*` routes registered | Possibly a **second** subscriber list beside MailPoet. See "Newsletter subscribers". |
| **Hosting** | not noticed | `wpcomsh/v1` + `wpcom/v2` | The site is hosted on **WordPress.com**. Changes how to ask for an uploads copy. |
| Namespaces | 58 | 56 | |

## Headline answers

| Question I.1 had to settle | Answer |
|---|---|
| Is the spike's `6311/v1` namespace installed? | **No.** `/wp-json/6311/v1/classes` → `404 rest_no_route`. |
| Is there an `exhibition` post type? | **No.** 23 post types registered, none is `exhibition`. Exhibitions have **no WordPress source**. |
| Is there an artists source? | **Yes, two that disagree:** an `artist` **taxonomy** on products (58 terms, 739 assignments) and the artist-named `product_cat` terms. Not a CPT. |
| Is `fooevents/v1/events` public? | **Yes** (`200`), **9** events — raw `WP_Post` rows with **no meta**, so no dates. |
| Does any public endpoint carry the FooEvents date meta? | **No.** `/wp/v2/product` exposes `meta`, but only Astra and Jetpack keys. The dates need `wc/v3`. |
| Is `wc/v3` reachable unauthenticated? | **No.** `401 woocommerce_rest_cannot_view`. A read-only WooCommerce key is genuinely required. |
| Is MailPoet present? | **Yes.** `mailpoet/v1` registers `GET /mailpoet/v1/subscribers`, admin-only. Getting the list is an admin CSV export — see "Newsletter subscribers". |
| Is 6311 already connected to Square? | **Yes.** `sws/v1` is a WooCommerce↔Square sync plugin (inventory, bulk-match, orders, customers, loyalty, webhooks). |
| Is there a sitemap? | **Yes, but not where WordPress core puts it.** `robots.txt` points to `/sitemap.xml` (Jetpack). |

## Record counts (2026-10-22)

| Endpoint | Count | Source |
|---|---|---|
| `wp/v2/media` | **4,138** | `X-WP-Total` |
| `wp/v2/pages` | **23** | `X-WP-Total` |
| `wp/v2/posts` | **0** | `X-WP-Total` — there is no blog |
| `wp/v2/product` | **873** | `X-WP-Total` — every published product |
| `wc/store/v1/products` | **797** | `X-WP-Total` — catalog-visible products only |
| `wc/store/v1/products?type=variable` | **99** | `X-WP-Total` |
| `wc/store/v1/products/categories` | **68** | response length (non-empty `product_cat` terms only) |
| `fooevents/v1/events` | **9** | response length (the endpoint takes no arguments) |
| `artist` taxonomy | **58** terms, **739** assignments | `X-WP-Total`, sum of `count` |
| `medium` taxonomy | **451** terms | `X-WP-Total` |
| `product_cat` taxonomy | **126** terms, **1,191** assignments, 63 empty | `X-WP-Total`, sum of `count` |
| `product_tag` taxonomy | **61** terms | `X-WP-Total` |
| `product_brand` taxonomy | **0** | `X-WP-Total` |
| `jp_pay_product` | **0** | `X-WP-Total` — Jetpack Payments buttons are not in use |
| `/sitemap-1.xml` | **729** URLs: 706 `/product/*` + 23 pages | `<loc>` count |

**Three product counts, three meanings:** 873 published (`wp/v2/product`) ⊃ 797 catalog-visible (Store API) ⊃
706 in the sitemap. The 76 the Store API hides are most likely catalog-visibility "hidden" products or
out-of-stock products under a "hide out of stock" setting — this pass did not distinguish them (the probe is
`wc/store/v1/products?stock_status=outofstock`). Either way, **I.3 must enumerate products from `wp/v2/product`
(and `wc/v3` once keyed), never from the Store API or the sitemap**, and every one of the 873 still owns a
`/product/<slug>` URL.

## Namespaces registered (56)

The ones that matter: `wp/v2`, `wc/store/v1`, `wc/v3`, `wc/v1`, `wc/v2`, `fooevents/v1`, `mailpoet/v1`,
**`sws/v1`**, `cptui/v1`, `wc/pos/v1/catalog`, `fl-controls/v1` (Beaver Builder), `astra/v1` + `astra_addon/v1`,
`jetpack/v4` + `jetpack-boost/v1` + `videopress/v1`, `wpcomsh/v1` + `wpcom/v2` + `wpcom/v3` (WordPress.com),
`search-filter/v1` + `search-filter-pro/v1`, `wc-facebook/v1`, `wc/gla` (Google Listings), `wcshipping/v1`,
`google-site-kit/v1`, `akismet/v1`, `metricool/v1`. The root index lists 1,823 routes.

Worth noting beyond the import:

- **`wc/pos/v1/catalog`** (2 routes) — WooCommerce's own POS catalog. With `sws/v1` also present, there are two
  candidate point-of-sale paths. Phase 5 should confirm with staff which one the counter actually uses.
- **`fl-controls/v1` and the `fl-builder-template` post type** — Beaver Builder. Bodies may carry builder
  markup, which `import.md` already requires stripping and reporting. (The `about` page sampled has none: its
  body is 188 characters of plain HTML.)

## Square is already connected (`sws/v1`)

`sws/v1` registers 169 routes, all admin-gated by their shape and **none called** in this pass. Grouped:

| Group | Routes | What it tells us |
|---|---|---|
| `square-inventory`, `inventory-sync`, `stock-checker` | 28 | WooCommerce products are imported from / exported to the Square catalog, and stock is synced. |
| `bulk-match`, `matcher`, `tools/missing-square`, `tools/scan-orphaned-square` | 18 | The plugin keeps its own WooCommerce↔Square id links and has orphan/duplicate repair tools. |
| `orders`, `square-orders` | 5 | Orders flow between the two. |
| `customers` | 14 | Customers are matched and imported. |
| `loyalty` | 6 | **Square Loyalty is in use or available.** Not in our scope list — ask staff. |
| `discount-codes` | 9 | WooCommerce coupons sync to Square discounts. |
| `webhook` | 10 | Square webhooks are already pointed at the WordPress site. |
| `settings`, `square`, `tools`, `logs`, … | rest | Token storage, locations, gateway, cron. |

Consequences:

- **I.9's Square match pass is matching against objects this plugin created.** That is the case
  `import.md`'s "Square matching" section was written for, and it is now confirmed rather than hypothetical. The
  WooCommerce SKUs (`5114915`, `182139T`, `X818145`, …) look Square-generated; SKU-first matching should hit
  most artwork.
- **The plugin's links are not readable to us.** They live in WooCommerce product meta, which only `wc/v3`
  `meta_data` exposes. The read-only WooCommerce key may surface them (a Square item/variation id per product);
  if it does, I.9 can link by id instead of by SKU. Check this the first time I.3 runs with the key.
- **Cutover has a new step:** the `sws` plugin's Square webhooks must be removed (or the plugin deactivated) when
  our gateway takes over, or Square will deliver every event to both sites, and the plugin will keep pushing
  WooCommerce edits into the catalog our CMS now owns. Add it to I.14's freeze checklist.

## Post types (23 registered)

None is an exhibition. The content-bearing ones are `page`, `product` and `attachment`. The rest are plugin
or theme machinery: `feedback` (Jetpack **form responses** — personal data, never read, never imported),
`jetpack_form`, `fl-builder-template`, `mailpoet_email`, `wp_knowledge`, `jp_act_log_event`,
`jp_pay_order`/`jp_pay_product` (0 products), the Jetpack search templates, and core's
block/template/navigation/font types.

`post` exists but holds **0** records.

## Taxonomies (10 registered)

| Taxonomy | On | Terms | Hierarchical | Note |
|---|---|---|---|---|
| **`artist`** | `product` | 58 | No | An artist roster. `rest_base: artist`. Public archives at `/artist/<slug>/`. |
| **`medium`** | `product` | 451 | No | "Mediums". **Unusable as-is** — see below. |
| `product_cat` | `product` | 126 | Yes | Mixes artist names *and* product types. The spike's deny-list problem. |
| `product_tag` | `product` | 61 | No | Present on classes (7857 has one). Not mapped to anything yet. |
| `product_brand` | `product` | 0 | Yes | Unused. |
| `category`, `post_tag` | `post` | — | — | Irrelevant: 0 posts. |
| `nav_menu`, `wp_pattern_category`, `wp_knowledge_type` | — | — | — | Machinery. |

### The two artist rosters disagree

`artist` taxonomy: 58 terms, 739 assignments. **0 of 58 carry a description**, so there is no artist bio
anywhere on the site — `Artist.Bio` has no source and staff must write it.

`product_cat`: 111 artist-named terms (126 minus the 15 product-type terms below), **61 of them with
`count: 0`** — half that roster has never had a piece listed.

Neither is complete:

| In `product_cat` only (with products) | In `artist` only (with products) |
|---|---|
| **`steve-oconnell` (33)** — no `artist` term at all | `jesy-grose` (5) |
| `michael-winchester` (28) | `mike-winchester` (33) |
| `hedda-lupin` (14) | `hedda-lubin` (18) |
| `anneterpstra` (5) | `anne-terpstra` (5) |
| | `audrey-avrilmedium` (1) — a mis-paste of `audrey-avril` |

Other data quality in the `artist` taxonomy:

- `6311arts` (4 products) — the gallery itself, not a person.
- `socorro-mucino` (38) **and** `sorocco-mucino` (0) — a misspelling.
- `jesy-grose` (5) **and** `jesy-grose-tigerbtch` (26) — one artist, two terms.
- **7 empty terms:** `adrian-zavala`, `alejandra-guerrero`, `eddwin-meyers`, `hector-hernandez`,
  `jeffrey-brooks`, `nicold-pittman`, `sorocco-mucino`.

Spellings disagree between the two: `michael-winchester` / `mike-winchester`, `hedda-lupin` / `hedda-lubin`,
`edwinn-meyers` / `eddwin-meyers`, `anneterpstra` / `anne-terpstra`.

### `product_cat` structure

Product-type terms: `artists` (134), `prints` (203), `jewelry` (89), `ceramics` (20), `t-shirts` (18),
`lamps` (11), `classes-workshops-events` (7) with children `classes` (4), `workshops` (2), `events` (1),
`books-magazines` (4), `memberships` (3), `branded-merchandise` (2), `class-supplies` (0), `uncategorized` (0).

The `artists` parent (1445) is applied **inconsistently**: 13 artist terms sit under it, two (`jeanne-reilly`,
`mary-laskey`) sit under **`jewelry`** (1390), and the rest have `parent: 0`. A hierarchy check will not separate
artists from product types, which is why the spike needed a deny-list.

### The `medium` taxonomy cannot be imported as a medium

451 terms, because staff have been pasting whole descriptions into the field. Real term slugs:

- `4-inch-bowl-showcases-the-captivating-beauty-of-wood-in-motion-flowing-worm-lines-filled-with-black-resin-trace-organic-paths-across-the-surface`
- `aaa-grade-freshwater-baroque-pearl-18k-gold-filled-paperclip-chain-hand-wire-wrapped-one-of-a-kind-pearl-natural-variations-in-shape`
- `and-designed-for-everyday-use`, `and-timeless`, `blend-seamlessly`
- `b`, `aluminum-dfimensions`, `automative-paints`, `brass-w`

Alongside perfectly good ones: `ceramic`, `ceramics`, `copper`, `acrylic`, `bronze`. Mapping this anywhere
needs a human-curated allow-list, not a transform.

## Products — what the public API gives, and what it withholds

`wc/store/v1/products` returns, per product: `id`, `slug`, `name`, `permalink`, **`sku`**, `prices`
(`price`/`regular_price`/`sale_price` as **strings in minor units** with `currency_minor_unit: 2`),
`categories` (`product_cat` only), `tags`, `brands`, `attributes`, `images[]` (`id`, `src`, `thumbnail`,
`srcset`, **`alt`**), `is_in_stock`, `low_stock_remaining`, `stock_availability.text`, `sold_individually`,
`description`, `short_description`, `type`, `variations`.

It **does not** return the `artist` or `medium` taxonomies, an exact stock quantity, or variation detail.

`/wp/v2/product` **is public** and fills the taxonomy gap: `artist`, `medium`, `product_cat`, `product_tag`
term ids plus `featured_media`, `title`, `content`, `excerpt`, `date`, `modified`, `link`, `status`. Its
`meta` is **only** Astra and Jetpack keys — 35 of them, none WooCommerce, none FooEvents. Two are SEO fields
(`jetpack_seo_html_title`, `advanced_seo_description`) — empty on the class sampled, but I.11's SEO-parity
check should read them where set.

So the public pair `wc/store/v1/products` + `wp/v2/product` covers everything except **exact stock**,
**FooEvents dates** and **variations**.

Observed stock signal: `stock_availability.text` is human prose — `"1 in stock"`, `"8 in stock"`, or `""`.
`low_stock_remaining` is `1` for single pieces and `null` otherwise. Parsing prose for a starting quantity is
not acceptable; `wc/v3` gives the integer.

Samples confirming the shapes (10-21 and 10-22):

| Product | id | type | SKU | price | stock | images | artist tax |
|---|---|---|---|---|---|---|---|
| Moonlight (jewelry) | 5665 | simple | `5114915` | `14375` | `1 in stock`, low=1 | 1, alt present | `[2029]` |
| 3 Shapes Long Necklace | 4700 | simple | `182139T` | `14000` | `1 in stock`, low=1 | 1, alt present | `[2019]` |
| Sketchbook | 8026 | simple | `X818145` | `1000` | in stock | **0 images** | `[2718]` |
| Boat Flags | 8145 | **variable** | *(empty)* | `190000` | `""` | — | — |
| 6311 Tote Bags (merch) | 2729 | simple | `629358P` | — | `7 in stock` | — | — |
| gray membership | 3338 | simple | *(empty)* | `8000` | `""`, sold individually | 1, **no alt** | — |
| Relief Printmaking (class) | 7857 | simple | *(empty)* | `30000` | `8 in stock` | 1 flyer, **no alt** | `[]` |
| Express Screen Printing (class) | 8177 | simple | *(empty)* | `7500` | `8 in stock` | **0 images** | — |

Artwork has SKUs; **classes, memberships and variable products have none** at the parent level — the CMS will
suggest `CLS-…` and `ART-…` for those.

### Variable products (99)

One in eight catalog-visible products is `variable`. The Store API gives the parent's lowest price and a list
of variation ids, but **the variation attributes come back empty** (`Boat Flags`: 2 variations, `attributes: []`)
— no names, prices, SKUs or stock per variation. Only `wc/v3/products/<id>/variations` has them.

`ShopItem` has one `StartingPriceCents` and no notion of variations, so these 99 need a mapping decision before
I.7 (see the recommendation). Note too that `sws/v1` has a `tools/scan-variation-attributes` route — variation
attributes may already be a known mess on the Square side.

## Classes — the attributes are not what the legacy plugin reads

The FooEvents products carry these **custom** (non-taxonomy) attributes:

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

`fooevents/v1/events` returns 24 raw `WP_Post` columns (`ID`, `post_name`, `post_title`, `post_content`,
`post_status`, `post_modified`, `post_author`, …) and **no meta**. Every row is `post_type: product`, none is
password-protected. Its other 10 routes are `POST`-only ticket-scanning endpoints for the FooEvents check-in app
(`login_status`, `get_tickets_in_event`, `update_ticket_status`, …) — never call them; they handle attendee data.

The 9 events (2026-10-22):

| id | slug | title |
|---|---|---|
| 8177 | `express-screen-printing-workshop-saturday-10am-1pm` | Express Screen Printing Workshop, Saturday, 10AM-1PM |
| 7857 | `intro-to-screen-printing-sundays-11-8-10am-1pm-5-week-course` | Relief Printmaking, Sundays, 11/8, 10AM-1PM, 5-Week Course |
| 7855 | `intermediate-screen-printing-with-diego-from-rathaus-wednesdays-11-4-630-930pm-5-week-course` | Intermediate Screen Printing with Diego from Rathaus, Wednesdays, 11/4, 6:30-9:30PM, 5-Week Course |
| 7735 | `printmaking-playground-sundays-11-8-200-500pm` | Printmaking Playground, Sundays, 11/8, 2:00-5:00PM 5-Week Course |
| 7662 | `intro-to-screen-printing-saturday-10-17-200-500pm` | Intro to Screen Printing-Saturdays, **11/7** 2:00-5:00PM 5-week Course |
| 7660 | `day-of-the-dead-ofrenda-alter-making-workshop-with-candice-thursday-11-22-600-900pm` | Day of the Dead-Ofrenda/Alter making Workshop-Thursday, 10/22 6:00-9:00pm |
| 7653 | `intro-to-scratchboard-friday-11-6-630-930pm` | Intro to Scratchboard - Friday, 11/6 6:30-9:30pm |
| 4327 | `boats-and-pianos-opening-reception` | "Boats & Pianos" Opening Reception |
| 4275 | `bonsai-101-living-sculpture` | Bonsai 101 - Living Sculpture Sunday 10/25 2-4pm |

**Staff reuse class products by re-dating the title.** 7662's slug says `10-17`; on 10-21 its title said
`10/24`; on 10-22 it says `11/7`. 7857's slug says "intro to screen printing", its title says "Relief
Printmaking". 7660's slug says `11-22`, its title `10/22`. Consequences: the title is the content, the slug is
only an address, **neither is a reliable source for a date** — the FooEvents meta is — and one WooCommerce
product can stand for several runs over its life. I.13's delta must expect a class product's date to change
between extracts.

## Memberships

3 products in `memberships`: `gray-membership` (`8000`), `black-membership` (`12000`),
`blue-membership` (`16000`). No SKU, no attributes, `sold_individually: true`, one image each with **no alt
text**.

Perks are **prose in the description**, not a list:

> "Support 6311arts community art center with a Gray membership for just $80 a year you can get 5% off all
> workshops and classes, an invite to the annual membership party, and a 6311arts tote. *Purchasing this
> membership subscribes you to automatic annual payment cycles…"

So the discount (`5%`) and the cadence (annual) are stated but unstructured, and `MembershipPlan.Perks` is an
ordered list of rows. This mapping needs staff input, not a transform.

**Who runs the "automatic annual payment cycles" is unknown.** No WooCommerce Subscriptions namespace is
registered. The `member_panel`, `guest`, `thank_you`, `cancel_payment` pages suggest a membership plugin; the
`sws` plugin's Square customer/order sync suggests the renewals might be Square subscriptions already. This
matters at cutover: existing members' renewals must keep working when the WordPress site goes away. Ask staff
before Phase 5's membership prompts.

There is also a `/gray-membership/` **page** (id 3345) separate from the product.

## Newsletter subscribers

**MailPoet** is installed. `GET /mailpoet/v1/subscribers` exists but is admin-only, and was not called. Reading
it over REST would need an administrator credential in the importer, which this project does not hold — the I.2
Application Password is a read-only user and cannot see MailPoet data. **The subscriber list comes from an admin
CSV export, by a person, not from the importer** (`import.md` "Personal data"; prompt I.10).

How to get it:

1. In wp-admin, **MailPoet → Subscribers → Export**.
2. Choose the lists to export and **CSV**.
3. Include email, first and last name, **status**, and **every date column offered** (subscribed / confirmed /
   created). The confirmed or subscribed date is the consent evidence; a row without one is skipped by I.10.
4. Save it under `artifacts/mailpoet/<YYYY-MM-DD>/` — `artifacts/` is gitignored. Never put it in `docs/`,
   email it, or commit it. Delete it after I.10 loads it, and record the deletion date in the cutover runbook.

**Possibly a second list:** the WordPress.com/Jetpack newsletter routes (`wpcom/v2/subscribers/*`) are
registered. That does not prove anyone subscribed through them, but if staff ever used a Jetpack "Subscribe"
block, those subscribers live in the WordPress.com dashboard, not MailPoet, and need their own export (with
subscription dates) into the same folder.

Two things I.10 must account for in the code as it stands:

- The entity is **`NewsletterSubscriber`** (`Forms` module), not `NewsletterSignup` as the I.10 prompt says. It
  has `ConsentedAt`, `ConsentVersion` and `Source`.
- `NewsletterSource` has `Signup` and `Submission` only. I.10 needs an **`Imported`** value, and a
  `ConsentVersion` that names the MailPoet origin rather than the site's current wording.

## Pages (23)

Content pages: `about`, `artists`, `events`, `gallery`, `home` (served at `/`), `memberships`, `shop`,
`privacy-policy`, `submit-your-work`, `gray-membership`, `classes-test-page`.

Plugin and account pages: `cart`, `checkout`, `my-account`, `login`, `register`, `forgot_password`,
`change_password`, `guest`, `member_panel`, `thank_you`, `cancel_payment`, `refund_returns`.

- **`privacy-policy` exists** (id 3137) with a substantial body.
- **`terms-and-conditions` does not exist** (`X-WP-Total: 0`, re-confirmed). The spike's `/terms` has no source;
  staff must write it.
- `about` (id 1) is 188 characters of plain HTML — no Beaver Builder markup, no shortcodes, no featured image.

## Media

4,138 attachments. `/wp/v2/media` gives `source_url` as the **bare origin URL**
(`https://6311arts.com/wp-content/uploads/2026/10/Red-Sail-Nook-Boat.jpeg`), not the Photon CDN form, so I.4
can download originals directly. It also gives **`filesize`** per original (797,991 bytes for a 1920×2560
JPEG in the sample) — so I.3's media JSON yields the exact download size before I.4 fetches a byte.

`alt_text` is populated on both items sampled (`"Red Sail Nook Boat"`, `"Nexus Jewelry 17"`), but product
**images** on classes and memberships have none. The real missing-alt figure comes from the snapshot.

`media_details.sizes` holds **11** generated sizes per image, all skipped per `import.md`.

Product image URLs inside Store API responses **are** Photon-wrapped
(`https://i0.wp.com/6311arts.com/wp-content/uploads/…?fit=…&ssl=1`); the media library's `source_url` is not.
Prefer the media library.

## Old-URL inventory for the redirect plan

`robots.txt` names `/sitemap.xml` and `/news-sitemap.xml`. `/sitemap.xml` is a Jetpack index →
`/sitemap-1.xml` (729 URLs: **706 `/product/*`** + the 23 pages) and `/image-sitemap-index-1.xml`.

Shapes to redirect, confirmed live:

- `/product/<slug>` — **873** published products own one (706 are in the sitemap). One namespace shared by
  artwork, classes, memberships and merch; only the mapping that claims each product can say where it goes.
- **`/artist/<slug>/`** — **public**: `/artist/6311arts/` → `200`, self-canonical, with a JSON alternate
  `wp/v2/artist/1988`. 58 terms, so up to 58 redirects (the empty and misspelled ones can share a target).
- `/product-category/<slug>` — 126 terms' archives (`link` is present on every term).
- `/about/`, `/shop/`, `/events/`, `/gallery/`, `/artists/`, `/memberships/`, `/privacy-policy/`,
  `/submit-your-work/`, `/gray-membership/`, `/classes-test-page/`
- `/cart/`, `/checkout/`, `/my-account/`, `/login/`, `/register/`, `/forgot_password/`, `/change_password/`,
  `/guest/`, `/member_panel/`, `/thank_you/`, `/cancel_payment/`, `/refund_returns/`

`robots.txt` also disallows `/wp-admin/`, `/*?add-to-cart=`, `/wp-content/uploads/wc-logs/`,
`/wp-content/uploads/woocommerce_transient_files/` and `/wp-content/uploads/woocommerce_uploads/`.

## Where the legacy plugin disagrees with reality

| `6311-headless.php` assumes | Live site |
|---|---|
| `6311/v1/classes|exhibitions|artists` serve the site | `404` — never installed |
| An `exhibition` post type exists (it registers one) | No such type. No exhibition content at all. |
| Artists are `product_cat` terms minus a 13-slug deny-list | A dedicated **`artist` taxonomy** exists too; the two rosters disagree and **neither is complete** |
| Artist `medium` comes from the term description | Every `artist` term description is **empty**; a separate `medium` taxonomy exists but is 451 junk-ridden terms |
| Class attributes are `instructor`, `type`, `level`, `sessions`, `cadence`, `capacity` | `Instructor`, `Level`, `Day of the Week`, `Time of Day`, `Class Type`. **No sessions, cadence or capacity.** |
| `capacity` is an attribute, `spotsLeft` is stock | No capacity anywhere; stock is prose in `stock_availability.text` |
| Classes are products in `classes-workshops-events` | That category has 7 (`classes` 4, `workshops` 2, `events` 1); `fooevents/v1/events` lists **9** |
| Only upcoming classes matter (it drops anything >24h past) | Past classes still own `/product/<slug>` URLs, and products are re-dated and reused |

## Verify

A re-run should still show: `6311/v1` → 404, `wc/v3` → 401, `fooevents/v1/events` → 200 with no meta,
no `exhibition` post type, an `artist` taxonomy of ~58 terms, and `sws/v1` registered. If any of those flip,
the mappings in I.7 need revisiting before they run. Counts are expected to drift daily (media, products,
events); treat a change of more than ~10 % as a reason to re-read this report.

## Roll back

Nothing to roll back. Every call was a GET and nothing was written to the live site.

---

## Recommendation

### 1. A read-only WooCommerce key is required. Get one.

It is the **only** way to reach what the import cannot do without:

- **FooEvents date and time meta** (`wc/v3/products?…` `meta_data`) — the start and end of every class session.
  Confirmed absent from `wp/v2/product`'s `meta`, from `wc/store/v1`, and from `fooevents/v1/events`.
- **Exact stock quantity** — the starting quantity and the one-of-a-kind flag. The public API gives only
  `"1 in stock"` prose and a `low_stock_remaining` hint.
- **Variations** of the 99 variable products — name, price, SKU and stock per variation.
- **The 76 products the Store API hides**, with full WooCommerce detail.
- **Probably, the `sws` plugin's Square ids** in product `meta_data`, which would let I.9 link by id.

Scope it to **Read** only, as I.2 specifies, and revoke it at I.14.

### 2. An Application Password is **not** needed.

Everything else the import reads is public: pages, media (with `alt_text` and `filesize`), products, the
`artist`, `medium` and `product_cat` taxonomies, and per-product taxonomy assignment via `/wp/v2/product`. The
admin-gated things are `feedback` (form responses — never imported), `wp_knowledge`, the `sws` plugin, and
MailPoet subscribers, which a read-only user cannot see anyway and which come from a CSV export (I.10).

So I.2 should create **one** credential, not two. If a later prompt finds a gap, add the Application Password
then.

### 3. A WXR export is not worth requesting. An uploads copy might be — and the host is WordPress.com.

- **WXR: no.** The REST API covers every content type we import, with `modified` timestamps for I.13's delta.
  WXR would add nothing but a second parser.
- **Uploads copy: worth one ask.** 4,138 originals, a few GB (sum `filesize` from I.3's media JSON for the exact
  figure), over 4,138 HTTPS requests at two concurrent. The site is on **WordPress.com**, so the ask goes to
  whoever administers that account, not a traditional host: on plans that allow it, SFTP access to
  `wp-content/uploads/`, or the dashboard's media-library export. Exclude `wc-logs/`, `woocommerce_uploads/` and
  `woocommerce_transient_files/`. If it's awkward, I.4's resumable downloader handles it either way.

### 4. Five decisions before I.7 can run

- **Artists: merge both rosters.** Neither the `artist` taxonomy nor `product_cat` is complete —
  `steve-oconnell` (33 products) exists only in `product_cat`. Recommendation: build the roster from the
  **union**, through an explicit merge table in I.7's mapping (`hedda-lupin`/`hedda-lubin`,
  `michael-winchester`/`mike-winchester`, `anneterpstra`/`anne-terpstra`, `edwinn-meyers`/`eddwin-meyers`,
  `jesy-grose`/`jesy-grose-tigerbtch`, `socorro-mucino`/`sorocco-mucino`, `audrey-avril`/`audrey-avrilmedium`),
  skip `6311arts` and the zero-count terms, and use both term sets to build redirects.
- **Variable products: what is a variation of a piece of art?** 99 of them. Options: one `ShopItem` per
  variation; one `ShopItem` from the parent with the variations listed for staff; or skip and review. Look at a
  handful through `wc/v3` (after I.2) before choosing; until then, mark them `NeedsReview` rather than guess.
- **Classes: sessions have no dates.** Even with the WooCommerce key, FooEvents gives one date per product, and
  staff re-date the same product for new runs. A "5-Week Course" has weeks 2–5 nowhere in the data.
  Recommendation: import one `ClassSession` from the current FooEvents date and mark the run `NeedsReview`, per
  `add-import-mapping`'s "never a guess".
- **Merch: which categories?** `branded-merchandise` (2) and `t-shirts` (18) are merch. `lamps` (11),
  `class-supplies` (0) and `books-magazines` (4) are judgement calls. Name the merch set explicitly in I.7's
  mapping table.
- **Memberships: who bills renewals today?** Answer before Phase 5's membership work and before cutover, so
  existing members' annual renewals don't silently stop.

### 5. Add to the cutover checklist (I.14)

- Disconnect the `sws` plugin's Square webhooks (or deactivate the plugin) when the gateway takes over Square
  webhooks, so Square events don't go to both sites and WooCommerce edits stop reaching the catalog.
- Delete the MailPoet (and any Jetpack) subscriber CSVs after I.10, and record the date.
