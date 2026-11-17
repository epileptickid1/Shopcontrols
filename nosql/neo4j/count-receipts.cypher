MATCH (p:ArchiveProduct {dataset: $dataset, productId: $productId})
OPTIONAL MATCH (p)<-[:CONTAINS]-(r:ArchiveReceipt {dataset: $dataset})
RETURN p.productId AS productId, count(DISTINCT r) AS receipts
