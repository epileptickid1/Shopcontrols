MATCH (r:ArchiveReceipt {dataset: $dataset, saleId: $receiptId})-[:CONTAINS]->(p:ArchiveProduct)
RETURN r.number AS number, p.productId AS productId, p.name AS product
ORDER BY productId
