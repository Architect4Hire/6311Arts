# legacy/ — importer reference only

`6311-headless.php` is the read-only WordPress must-use plugin that the `spike0` prototype read from
(`/wp-json/6311/v1/classes`, `/exhibitions`, `/artists`, plus the Exhibition post type and its fields).

It is kept **only as a reference for `Arts6311.Importer`**: it documents the WordPress endpoints, post types
and meta keys that the WordPress → CMS mappings read (see `.claude/rules/import.md` and
`docs/prompts/wordpress-import-scrub-prompts.md`).

- It is not part of the Angular build. It's outside `src/` and `public/`, so nothing bundles or serves it.
- It is not deployed, and the new platform doesn't depend on it.
- Don't extend it. Once the cutover is done (I.14), it can be deleted.
