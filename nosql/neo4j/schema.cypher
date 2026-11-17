CREATE CONSTRAINT archive_check_key IF NOT EXISTS
FOR (r:ArchiveReceipt) REQUIRE (r.dataset, r.saleId) IS UNIQUE;

CREATE CONSTRAINT archive_product_key IF NOT EXISTS
FOR (p:ArchiveProduct) REQUIRE (p.dataset, p.productId) IS UNIQUE;
