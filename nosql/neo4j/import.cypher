UNWIND $rows AS row
MERGE (r:ArchiveReceipt {dataset: $dataset, saleId: row.id})
ON CREATE SET r.number = row.number, r.soldAt = datetime(row.soldAt)
WITH r, row
UNWIND row.items AS item
MERGE (p:ArchiveProduct {dataset: $dataset, productId: item.productId})
ON CREATE SET p.name = item.productName
MERGE (r)-[:CONTAINS]->(p)
