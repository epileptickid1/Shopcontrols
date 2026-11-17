MATCH (p:ArchiveProduct {dataset: $dataset, productId: $productId})<-[:CONTAINS]-
      (r:ArchiveReceipt {dataset: $dataset})-[:CONTAINS]->(other:ArchiveProduct {dataset: $dataset})
WHERE other <> p
RETURN other.productId AS productId, other.name AS product, count(DISTINCT r) AS sharedReceipts
ORDER BY sharedReceipts DESC, productId ASC
LIMIT 10
