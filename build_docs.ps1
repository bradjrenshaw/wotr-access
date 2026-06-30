# Builds the documentation book with mdbook.
# Requires mdbook on PATH (https://rust-lang.github.io/mdBook/). Output: docs_src/book/.
# Open docs_src/book/index.html in a browser, or run `mdbook serve docs_src` for live preview.
mdbook build "$PSScriptRoot/docs_src"
