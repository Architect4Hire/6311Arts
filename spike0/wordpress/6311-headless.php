<?php
/**
 * Plugin Name: 6311arts Headless Helpers
 * Description: Read-only REST endpoints the Angular front end uses for classes, exhibitions and artists, plus an Exhibition post type so staff edit shows in wp-admin. Also closes the public user list found in the 2026 audit.
 * Version:     1.0.0
 *
 * Install as a must-use plugin (wp-content/mu-plugins/6311-headless.php) or upload as a regular plugin.
 * Nothing here changes checkout, orders, FooEvents or WooCommerce data. It only reads.
 */

if (!defined('ABSPATH')) {
    exit;
}

/* -------------------------------------------------------------------------
 * Exhibitions: a simple post type with start/end dates, editable in wp-admin.
 * ---------------------------------------------------------------------- */

add_action('init', function () {
    register_post_type('exhibition', [
        'label'        => 'Exhibitions',
        'public'       => true,
        'show_in_rest' => true,
        'menu_icon'    => 'dashicons-format-gallery',
        'supports'     => ['title', 'excerpt', 'thumbnail', 'custom-fields'],
        'has_archive'  => false,
        'rewrite'      => ['slug' => 'exhibition'],
    ]);
    foreach (['start_date', 'end_date', 'reception_start', 'reception_end', 'reception_note', 'artists'] as $key) {
        register_post_meta('exhibition', $key, [
            'type'         => 'string',
            'single'       => true,
            'show_in_rest' => true,
        ]);
    }
});

/* -------------------------------------------------------------------------
 * GET /wp-json/6311/v1/classes      upcoming FooEvents products
 * GET /wp-json/6311/v1/exhibitions  all exhibitions
 * GET /wp-json/6311/v1/artists      artist product categories with counts
 * ---------------------------------------------------------------------- */

add_action('rest_api_init', function () {
    $public = ['methods' => 'GET', 'permission_callback' => '__return_true'];
    register_rest_route('6311/v1', '/classes', $public + ['callback' => 'six311_classes']);
    register_rest_route('6311/v1', '/exhibitions', $public + ['callback' => 'six311_exhibitions']);
    register_rest_route('6311/v1', '/artists', $public + ['callback' => 'six311_artists']);
});

/** Turns FooEvents' stored date and hour fields into an ISO 8601 date-time in the site's time zone. */
function six311_fooevents_time($product_id, $date_key, $hour_key, $min_key, $period_key) {
    $date = get_post_meta($product_id, $date_key, true);
    if (!$date) {
        return null;
    }
    $hour   = (int) get_post_meta($product_id, $hour_key, true);
    $min    = (int) get_post_meta($product_id, $min_key, true);
    $period = strtolower((string) get_post_meta($product_id, $period_key, true));
    if ($period === 'p.m.' || $period === 'pm') {
        $hour = $hour % 12 + 12;
    } elseif ($period === 'a.m.' || $period === 'am') {
        $hour = $hour % 12;
    }
    try {
        $dt = new DateTime($date, wp_timezone());
    } catch (Exception $e) {
        return null;
    }
    $dt->setTime($hour, $min);
    return $dt->format('c');
}

function six311_classes() {
    $cached = get_transient('six311_classes');
    if ($cached !== false) {
        return $cached;
    }

    $products = wc_get_products([
        'status'   => 'publish',
        'limit'    => 100,
        'category' => ['classes-workshops-events'],
    ]);

    $out = [];
    foreach ($products as $p) {
        $id    = $p->get_id();
        $start = six311_fooevents_time($id, 'WooCommerceEventsDate', 'WooCommerceEventsHour', 'WooCommerceEventsMinutes', 'WooCommerceEventsPeriod');
        if (!$start || strtotime($start) < time() - DAY_IN_SECONDS) {
            continue; // skip past or undated events
        }
        $end   = six311_fooevents_time($id, 'WooCommerceEventsDate', 'WooCommerceEventsHourEnd', 'WooCommerceEventsMinutesEnd', 'WooCommerceEventsEndPeriod') ?: $start;
        $stock = $p->managing_stock() ? (int) $p->get_stock_quantity() : null;
        $attrs = function ($name) use ($p) {
            $v = $p->get_attribute($name);
            return $v !== '' ? $v : null;
        };
        $image = wp_get_attachment_image_url($p->get_image_id(), 'large');

        $out[] = [
            'id'         => $id,
            'slug'       => $p->get_slug(),
            'title'      => wp_strip_all_tags($p->get_name()),
            'start'      => $start,
            'end'        => $end,
            'sessions'   => (int) ($attrs('sessions') ?? 1),
            'cadence'    => $attrs('cadence'),
            'type'       => $attrs('type') ?? 'Other',
            'level'      => $attrs('level') ?? 'All levels',
            'price'      => (int) round(((float) $p->get_price()) * 100),
            'instructor' => $attrs('instructor'),
            'image'      => $image ?: null,
            'summary'    => wp_strip_all_tags($p->get_short_description() ?: wp_trim_words($p->get_description(), 30)),
            'spotsLeft'  => $stock,
            'capacity'   => $attrs('capacity') ? (int) $attrs('capacity') : null,
        ];
    }

    usort($out, fn($a, $b) => strcmp($a['start'], $b['start']));
    set_transient('six311_classes', $out, 5 * MINUTE_IN_SECONDS);
    return $out;
}

function six311_exhibitions() {
    $posts = get_posts(['post_type' => 'exhibition', 'numberposts' => 100, 'post_status' => 'publish']);
    return array_map(function ($post) {
        $m = fn($k) => get_post_meta($post->ID, $k, true) ?: null;
        $reception = $m('reception_start')
            ? ['start' => $m('reception_start'), 'end' => $m('reception_end'), 'note' => $m('reception_note')]
            : null;
        return [
            'slug'      => $post->post_name,
            'title'     => get_the_title($post),
            'start'     => $m('start_date'),
            'end'       => $m('end_date'),
            'summary'   => wp_strip_all_tags(get_the_excerpt($post)),
            'artists'   => array_values(array_filter(array_map('trim', explode(',', (string) $m('artists'))))),
            'image'     => get_the_post_thumbnail_url($post, 'large') ?: null,
            'reception' => $reception,
        ];
    }, $posts);
}

/** Artists are WooCommerce product categories; anything that names a kind of product is left out. */
function six311_artists() {
    $not_artists = ['artists', 'ceramics', 'jewelry', 'prints', 'painting', 'sculpture', 'books-magazines',
        'branded-merchandise', 'classes', 'classes-workshops-events', 'events', 'memberships', 'uncategorized'];
    $terms = get_terms(['taxonomy' => 'product_cat', 'hide_empty' => false]);
    $out   = [];
    foreach ($terms as $t) {
        if (in_array($t->slug, $not_artists, true)) {
            continue;
        }
        $thumb = get_term_meta($t->term_id, 'thumbnail_id', true);
        $out[] = [
            'slug'         => $t->slug,
            'name'         => html_entity_decode($t->name),
            'medium'       => $t->description ? wp_strip_all_tags($t->description) : null,
            'productCount' => (int) $t->count,
            'image'        => $thumb ? wp_get_attachment_image_url($thumb, 'medium_large') : null,
        ];
    }
    usort($out, fn($a, $b) => strcasecmp($a['name'], $b['name']));
    return $out;
}

/* Clear the classes cache when a product changes, so new dates and sell-outs show up right away. */
add_action('woocommerce_update_product', fn() => delete_transient('six311_classes'));
add_action('woocommerce_product_set_stock', fn() => delete_transient('six311_classes'));

/* -------------------------------------------------------------------------
 * Audit fix: stop listing user accounts to the public.
 * ---------------------------------------------------------------------- */

add_filter('rest_endpoints', function ($endpoints) {
    if (!is_user_logged_in()) {
        unset($endpoints['/wp/v2/users'], $endpoints['/wp/v2/users/(?P<id>[\d]+)']);
    }
    return $endpoints;
});

/* Let the Angular site call the REST API from the browser during hydration. */
add_action('rest_api_init', function () {
    $allowed = ['https://6311arts.com', 'https://www.6311arts.com', 'http://localhost:4000', 'http://localhost:4200'];
    $origin  = get_http_origin();
    if ($origin && in_array($origin, $allowed, true)) {
        header('Access-Control-Allow-Origin: ' . $origin);
        header('Vary: Origin', false);
    }
}, 15);
