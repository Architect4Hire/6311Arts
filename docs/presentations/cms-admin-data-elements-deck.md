# 6311arts CMS — field inventory by object and screen

> **For the deck generator:**
> - Make one section (one or two slides) per object below.
> - Each slide shows the object's purpose, then its fields table: **Field**, **Type**, **Required**, **Purpose**.
> - Split a table across a "(continued)" slide if it has more than ~8 rows.
> - Sections 14–16 are comparison and summary slides. Render their tables as they are, and end the deck with section 16.
> - Use only the fields listed here.

**Legend for "Required":**
- **Yes:** needed to save.
- **Publish:** needed before the record can be published.
- **No:** optional.
- **Read-only:** shown but set by the system or by Square.

---

## 1. Fields every content record shares

Applies to Class, Exhibition, Artist, Instructor, Shop piece, Membership tier and Page.

| Field | Type | Required | Purpose |
|---|---|---|---|
| Status | Draft / Scheduled / Published / Archived | Read-only | Where the record is in its life: only staff see Drafts, Published records are live, Archived records are retired for good |
| Web address (slug) | Text, lower-case and hyphens | Yes | The record's address on the site, e.g. `/artists/jane-doe`. Changing it after publishing redirects the old address automatically |
| Publish at (scheduled for) | Day + time, Chicago time | No | Publishes the record automatically at a set time |
| Published at | Date/time | Read-only | When it was first published; decides whether the address is locked behind a redirect |
| Preview link | Temporary link | — | Lets someone read the draft as it will appear, for about 30 minutes |
| Created / Updated / Updated by | Date/time, person | Read-only | Audit trail of who changed the record and when |
| Old WordPress id | Number | Read-only | Ties an imported record back to the old site |

---

## 2. Staff user (top bar and navigation)

Purpose: identifies who is signed in and which sections they may use.

| Field | Type | Required | Purpose |
|---|---|---|---|
| Display name | Text | Read-only | Shown in the top bar and the dashboard greeting ("Welcome, …") |
| Role | Admin / Editor | Read-only | Decides which sections and actions the person sees. Admin adds Redirects, Sync, Archive and image Delete |

The **Dashboard** has no data fields yet. It shows a greeting, shortcuts and a placeholder for a future "attention list". **Forms** and **Sync** are placeholders with no fields yet.

---

## 3. Class

Purpose: describes a class or workshop as a course. When it meets, how many seats it has and what it costs live on its runs.

**List screen shows:**
- Title (with its address, kind and level)
- Next session
- Status
- Number of runs
- Instructors
- Last updated

| Field | Type | Required | Purpose |
|---|---|---|---|
| Title | Text (≤200) | Yes | The class name on the site and in Square |
| Web address | Slug (≤120) | Yes | `/classes/{address}` |
| Summary | Text (≤500) | Publish | One or two sentences for class cards and search results |
| Kind | Text (≤60) | No | Type of class, e.g. Printmaking, Drawing, Talk; used for grouping and display |
| Level | Text (≤60) | No | Who it suits, e.g. Beginner, All levels |
| More details | Rich text | No | The full class description, shown below the summary |
| Hero image | Image from Media | No (alt text needed to publish) | The main picture on the class page and card |
| Runs | List (1–30) | Publish (at least 1) | The terms in which the class is offered (see Class run) |

---

## 4. Class run

Purpose: one term of a class, with its own instructor, dates, seats and price. A seat is bought per run and covers every session in it.

| Field | Type | Required | Purpose |
|---|---|---|---|
| Label | Text (≤100) | No | Tells runs apart, e.g. "Fall 2026, Sunday mornings" |
| How often it meets | Text (≤100) | No | Plain-language schedule, e.g. "Saturdays for 5 weeks" |
| Instructor | Pick from Instructors | Publish | Who teaches this run |
| Seats | Number (1–500) | Yes | Class capacity. The CMS owns it, and Square tracks sales against it |
| SKU | Code (≤64), e.g. CLS-4-1 | No (suggested) | Stock code the front desk and Square use. Unique across the whole CMS |
| Price per seat | Money ($0–$10,000) | Publish | Starting price sent to Square when the run is created. Editable only until Square holds the run |
| Sessions | List (1–60) | Publish (at least 1) | The individual meetings (see Class session) |
| Price in Square | Money | Read-only (Square) | Square's current price for a seat |
| Seats left | Number | Read-only (Square) | Square's count of unsold seats |
| Sold out | Yes/No | Read-only (Square) | Whether Square reports the run as full |
| In Square | Yes/No | Read-only | Whether Square holds the run yet. Once it does, the run can't be removed and the starting price locks |

---

## 5. Class session

Purpose: one meeting of a class run.

| Field | Type | Required | Purpose |
|---|---|---|---|
| Date | Date | Yes | The day the session meets |
| Starts | Time (Chicago) | Yes | Start time shown to visitors |
| Ends | Time (Chicago) | Yes | End time; must be after it starts |

---

## 6. Exhibition

Purpose: a show at the gallery, with its dates, reception, hero image and artists.

**List screen shows:**
- Title (with its address)
- Dates
- Status
- Number of artists
- Last updated

| Field | Type | Required | Purpose |
|---|---|---|---|
| Title | Text (≤200) | Yes | The show's name |
| Web address | Slug (≤120) | Yes | `/exhibitions/{address}` |
| First day | Date | Yes | When the show opens; used for current vs upcoming |
| Last day | Date | Yes | When the show closes |
| Summary | Text (≤500) | Publish | Short text for cards and search results |
| About the show | Plain text (≤10,000) | No | The full write-up on the exhibition page |
| Opening reception | On/off | No | Whether the show has an opening event |
| Reception day | Date | Yes, if the reception is on | Day of the opening event |
| Reception starts / ends | Time (Chicago) | Yes, if the reception is on | When the event runs |
| Reception note | Text (≤200) | No | A line under the time, e.g. "Free and open to all" |
| Hero image | Image from Media | Publish (needs alt text) | The main picture for the show |
| Artists | Ordered list (≤50) | No | Artists credited in the show, in the order the site lists them |

---

## 7. Artist

Purpose: a member of the artist roster, credited on exhibitions and shop pieces.

**List screen shows:**
- Name (with its address)
- Medium
- Status
- Showing now
- Last updated

| Field | Type | Required | Purpose |
|---|---|---|---|
| Name | Text (≤200) | Yes | The artist's name as shown on the site |
| Web address | Slug (≤120) | Yes | `/artists/{address}` |
| Medium | Text (≤100) | No | What they make, shown under their name, e.g. "Ceramics" |
| Bio | Plain text (≤5,000) | No | Biography on the artist page |
| Shows at 6311arts now | Yes/No | No (default yes) | Separates current artists from past ones without unpublishing them |
| Portrait | Image from Media | No (alt text needed to publish) | Photo of the artist |

---

## 8. Instructor

Purpose: a person who teaches classes; each class run names one.

**List screen shows:**
- Name (with its address)
- Medium
- Status
- Teaching now
- Last updated

| Field | Type | Required | Purpose |
|---|---|---|---|
| Name | Text (≤200) | Yes | The instructor's name |
| Web address | Slug (≤120) | Yes | `/instructors/{address}` |
| What they teach | Text (≤100) | No | Shown under their name, e.g. "Printmaking" |
| Bio | Plain text (≤5,000) | No | Biography on the instructor page |
| Website | https:// address (≤300) | No | Link to their own site |
| Instagram | Handle (≤30) | No | Link to their Instagram |
| Teaches at 6311arts now | Yes/No | No (default yes) | Separates current instructors from past ones without unpublishing them |
| Portrait | Image from Media | No (alt text needed to publish) | Photo of the instructor |
| Also an artist | Pick from Artists | No | Links the instructor to their artist record when they are both |

---

## 9. Shop piece (artwork)

Purpose: a piece of artwork sold on the website and through Square. Merch is never in the CMS.

**List screen shows:**
- Title (with SKU, categories and "One of a kind")
- Artist
- Status
- Square price and stock
- Last updated

| Field | Type | Required | Purpose |
|---|---|---|---|
| Title | Text (≤200) | Yes | The piece's name |
| Web address | Slug (≤120) | Yes | `/shop/{address}` |
| SKU | Code (≤64), e.g. ART-17 | No (suggested) | Stock code for Square and the front desk. Unique across the CMS |
| Description | Rich text | Publish | Description on the piece's page |
| Categories | Any of: Jewelry, Prints, Ceramics, Painting, Sculpture, Books and magazines | No | Shop filtering and grouping |
| Artist | Pick from Artists | No | Who made it |
| Photographs | Ordered images (1–12) | Publish (at least 1, with alt text) | Pictures of the piece; the first is the lead photograph |
| Price | Money ($0–$50,000) | Publish | Starting price sent to Square when the piece is created. Editable only until Square holds it |
| One of a kind | Yes/No | No (default yes) | Marks a unique piece; quantity is fixed at 1 |
| How many | Number (1–1,000) | Yes, if not one of a kind | Starting stock count sent to Square |
| Price in Square | Money | Read-only (Square) | Square's current selling price |
| In stock | Number | Read-only (Square) | Square's current stock count |
| Sold out | Yes/No | Read-only (Square) | Whether Square reports none left |

---

## 10. Membership tier

Purpose: one membership level shown on the /memberships page and sold as a Square subscription.

**List screen shows:**
- Tier name (with Featured and number of perks)
- Status
- Price and cadence
- Class discount
- Last updated

| Field | Type | Required | Purpose |
|---|---|---|---|
| Name | Text (≤100) | Yes | The tier's name, e.g. "Gray membership" |
| Key | Slug (≤120) | Yes | A stable internal id for the tier (all tiers share one page, so it's not an address) |
| Perks | Ordered lines (1–12, each ≤200) | Publish (at least 1) | What the tier includes, in display order |
| Class discount (%) | Whole number (0–100) | No | What members of this tier save on classes |
| Order on the page | Whole number (1–100) | No | Position among the tiers; 1 comes first |
| Highlight this tier | Yes/No | No | Features the tier visually on the page |
| Price | Money ($0–$5,000) | Publish | What a member pays each billing period. Sent to Square once, at creation |
| Bills | Once a year / Every month | Yes | Billing cadence. Sent to Square once, at creation |
| Price in Square | Money | Read-only (Square) | Square's current plan price |
| Cadence in Square | Text | Read-only (Square) | How often Square bills, e.g. Annual |

---

## 11. Page

Purpose: a standalone page such as the privacy policy or terms.

**List screen shows:**
- Title (with its address)
- Status
- Last updated

| Field | Type | Required | Purpose |
|---|---|---|---|
| Title | Text (≤200) | Yes | The page heading |
| Web address | Slug (≤120) | Yes | `6311arts.com/{address}` (site root) |
| Text | Rich text | Publish | The page's content |

---

## 12. Image (Media library)

Purpose: one image in the shared library, used by every other object. It is described once and reused everywhere.

**Library screen shows tiles with:**
- Thumbnail and file name
- Dimensions and size
- Badges: Processing, Failed, Needs alt text, Published

| Field | Type | Required | Purpose |
|---|---|---|---|
| File | JPEG / PNG / WebP (≤25 MB) | Yes (at upload) | The original image. It is resized to 2,560 px, compressed and stripped of GPS data |
| Alt text | Text (≤500) | Publish (for content that uses it) | Describes the image for people who can't see it |
| Credit | Text (≤200) | No | Who made or owns the image, shown beside it |
| Compression | Balanced / High quality / Smallest | Yes (default Balanced) | Trades file size against detail for the website copies |
| File name | Text | Read-only | Identifies the upload |
| Dimensions, type, stored size | Facts | Read-only | Technical details and the space saved by compression |
| Processing status | Processing / Ready / Failed | Read-only | Whether the website sizes have been made |
| Visibility | Draft / Published | Read-only | Whether published content uses the image |
| Website sizes | List of sizes | Read-only | The resized copies the site serves (e.g. 400 and 800 wide) |
| Uploaded by / at | Person, date | Read-only | Audit |

---

## 13. Redirect (Admin only)

Purpose: sends visitors from an old address to its new home.

**List screen shows:**
- Old address
- Leads to
- Type
- Made by

| Field | Type | Required | Purpose |
|---|---|---|---|
| Old address | Path starting "/" (≤400) | Yes | The address people may still have bookmarked or linked |
| Leads to | "/…" or https:// address (≤800) | Yes | Where visitors are sent instead |
| Type | Permanent (301) / Temporary (302) | Yes (default Permanent) | Tells search engines whether the move is for good |
| Made by | Added by staff / From the old site / Address change | Read-only | Shows whether a person, the WordPress import or a slug change created it |
| Created / Updated / Updated by | Date/time, person | Read-only | Audit |

---

## 14. Class vs Exhibition

Purpose: shows how the two scheduled content types differ. An exhibition is close to "a class without a seat limit", but it is simpler still: it has no runs, sessions, seats, price or SKU, and nothing about it goes to Square.

| | Class | Exhibition |
|---|---|---|
| When | Runs, each with dated sessions (start and end times) | One date range: First day to Last day |
| Event | Every session is a meeting | One optional opening reception (day, start, end, note) |
| People | One instructor per run | An ordered list of up to 50 credited artists |
| Seats / capacity | Per run, 1–500 | None |
| Price / SKU / Square | Per run, synced with Square | None |
| Copy | Title, address, summary, kind, level, rich-text details | Title, address, summary, plain-text "About the show" |
| Image | Hero image | Hero image |

---

## 15. How the people records connect

Purpose: Artists and Instructors aren't scheduled things. They are people records that other objects point to.

| People record | Used by | How |
|---|---|---|
| Artist | Exhibition | Ordered list of credited artists (up to 50) |
| Artist | Shop piece | One credited artist per piece |
| Instructor | Class run | One instructor per run |
| Instructor | Artist | Optional "Also an artist" link when a person is both |

Archived artists and instructors can't be newly credited or chosen.

---

## 16. Not in the CMS today: general events

Purpose: records a gap for discussion.

- There is no general "event" object, such as a free talk or a drop-in with no seat limit.
- The closest fits today are:
  - an exhibition's opening reception
  - a class run priced at $0 (classes still need a seat count)
- A separate Event object would be a new requirement.
