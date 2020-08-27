using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Text;
using System.Threading;

internal class Chat
{
    // Set exchange name for chat channels to connect to
    private static readonly string exchangeName = "chatTestExchange";

    // Set the name of the queue that stores all sent messages
    private static readonly string storageQueueName = "storedMessagesQueue";

    // Declare these variables outside the Main method so other methods can use them too
    private static string chatName;
    private static IConnection connection;
    private static string randUserId;

    public static void Main()
    {
        Console.Clear();
        Console.Title = "Chat App Test";

        randUserId = new Random().Next(0, 9999999).ToString();

        // Set server settings here, comment out settings that aren't needed
        ConnectionFactory factory = new ConnectionFactory()
        {
            HostName = "localhost"
            //VirtualHost = virtualHost,
            //UserName = userName,
            //Password = password
        };

        connection = factory.CreateConnection();

        // Get chat name from user
        chatName = GetUserInput("Enter a name to use while chatting: ");

        // Use main thread to retrieve previous messages
        GetStoredMessages();

        // Create two separate threads for sending/receiving live messages
        Thread receivingThread = new Thread(new ThreadStart(StartReceiving));
        Thread sendingThread = new Thread(new ThreadStart(StartSending));

        sendingThread.Start();
        receivingThread.Start();
    }

    public static void GetStoredMessages()
    {
        // Create new channel, declare it to the fanout exchange, declare new queue and bind it to the exchange
        IModel channelReceiveStoredMsgs = connection.CreateModel();
        channelReceiveStoredMsgs.ExchangeDeclare(exchangeName, type: ExchangeType.Fanout);

        channelReceiveStoredMsgs.QueueDeclare(queue: storageQueueName,
                                 durable: true,
                                 exclusive: false,
                                 autoDelete: false,
                                 arguments: null);

        channelReceiveStoredMsgs.QueueBind(queue: storageQueueName,
                                 exchange: exchangeName,
                                 routingKey: "");

        // The steps above will be skipped if such a queue already exists

        // Create new consumer that will retrieve the messages from the storage queue without acknowledging them,
        // so they can be retrieved by other chatters as well.
        EventingBasicConsumer consumerStored = new EventingBasicConsumer(channelReceiveStoredMsgs);

        channelReceiveStoredMsgs.BasicConsume(queue: storageQueueName,
                             autoAck: false,
                             consumer: consumerStored);

        Console.WriteLine("\nHi {0}, previous messages are being retrieved... \n", chatName);

        // Keep count of number of messages that are retrieved
        int nMessages = 0;
        consumerStored.Received += (model, ea) =>
        {
            byte[] body = ea.Body.ToArray();
            string message = Encoding.UTF8.GetString(body);
            Console.WriteLine(message);
            nMessages++;
        };

        // Wait for all messages to load with a little shortcut
        Thread.Sleep(1000);

        // Close the channel when the messages have been retrieved (or if no messages were found)
        channelReceiveStoredMsgs.Close();

        if (nMessages > 0)
        {
            Console.WriteLine("\nPrevious messages retrieved!\n");
        }
        else
        {
            Console.WriteLine("No previous messages were found.\n");
        }
    }

    public static void StartSending()
    {
        // Create new channel for sending messages and bind it to the fanout exchange
        IModel channelSend = connection.CreateModel();
        channelSend.ExchangeDeclare(exchangeName, type: ExchangeType.Fanout);

        Console.WriteLine("You can start chatting! Type 'exit' to stop chatting.\n");

        var properties = channelSend.CreateBasicProperties();
        properties.UserId = randUserId;

        while (true)
        {
            Console.Write(chatName + ": ");

            // Listen for new messages
            string message = Console.ReadLine();

            // Listen for 'exit' to stop chatting
            if (message == "exit")
            {
                break;
            }

            // Add timestamp to the message
            string formattedMsg = FormatMessage(message);

            // Send message to the exchange without specifying a queue (fanout exchange sends it to all 
            // queues.
            byte[] body = Encoding.UTF8.GetBytes(formattedMsg);
            channelSend.BasicPublish(exchange: exchangeName,
                                    routingKey: "",
                                    basicProperties: properties,
                                    body: body);

            // Write the sent message over the console input to prevent double messages from appearing
            Console.SetCursorPosition(0, Console.CursorTop - 1);
            Console.WriteLine(formattedMsg);
        }
    }

    public static void StartReceiving()
    {
        // Create new channel for receiving live messages and bind it to the exchange
        IModel channelReceive = connection.CreateModel();
        channelReceive.ExchangeDeclare(exchangeName, type: ExchangeType.Fanout);

        // Create a temporary queue to receive live messages and bind it to the exchange
        string tempQueueName = channelReceive.QueueDeclare().QueueName;

        channelReceive.QueueBind(queue: tempQueueName,
                                 exchange: exchangeName,
                                 routingKey: "");

        // Start receiving live messages
        EventingBasicConsumer consumer = new EventingBasicConsumer(channelReceive);

        channelReceive.BasicConsume(queue: tempQueueName,
                             autoAck: true,
                             consumer: consumer);

        consumer.Received += (model, ea) =>
        {
            byte[] body = ea.Body.ToArray();

            string message = Encoding.UTF8.GetString(body);

            // Messages sent by the user are received by the user too, so filter out those 
            // messages before writing them to the console to prevent double messages from appearing

            if (ea.BasicProperties.UserId != randUserId)
            {
                Console.WriteLine("\r" + message);
                Console.Write(chatName + ": ");
            }
        };
    }

    public static string FormatMessage(string message)
    {
        string currentTime = DateTime.Now.ToString().Split(" ")[1];
        return currentTime + " " + chatName + ": " + message;
    }

    public static string GetUserInput(string question)
    {
        Console.Write(question);
        return Console.ReadLine();
    }
}